/*
The MIT License (MIT)

Copyright (c) 2007 - 2019 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace ServiceMonitoringPlugin.Handlers
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Helpers;
    using Messages;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Dto;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eForm.Infrastructure.Models;
    using Microting.EformMonitoringBase.Infrastructure.Data;
    using Microting.EformMonitoringBase.Infrastructure.Models.Blocks;
    using Microting.EformMonitoringBase.Infrastructure.Models.Settings;
    using Newtonsoft.Json;
    using Rebus.Handlers;

    public class EFormCompletedHandler : IHandleMessages<EformCompleted>
    {
        private readonly eFormCore.Core _sdkCore;
        private readonly EformMonitoringPnDbContext _dbContext;

        public EFormCompletedHandler(eFormCore.Core sdkCore, EformMonitoringPnDbContext dbContext)
        {
            _dbContext = dbContext;
            _sdkCore = sdkCore;
        }
        
        public async Task Handle(EformCompleted message)
        {
            try
            {
                // Get settings
                var settings = await _dbContext.PluginConfigurationValues
                    .ToListAsync();
                var sendGridKey = settings.FirstOrDefault(x =>
                    x.Name == nameof(MonitoringBaseSettings) + ":" + nameof(MonitoringBaseSettings.SendGridApiKey));
                if (sendGridKey == null)
                {
                    throw new Exception($"{nameof(MonitoringBaseSettings.SendGridApiKey)} not found in settings");
                }
                var fromEmailAddress = settings.FirstOrDefault(x =>
                    x.Name == nameof(MonitoringBaseSettings) + ":" + nameof(MonitoringBaseSettings.FromEmailAddress));
                if (fromEmailAddress == null)
                {
                    throw new Exception($"{nameof(MonitoringBaseSettings.FromEmailAddress)} not found in settings");
                }
                var fromEmailName = settings.FirstOrDefault(x =>
                    x.Name == nameof(MonitoringBaseSettings) + ":" + nameof(MonitoringBaseSettings.FromEmailName));
                if (fromEmailName == null)
                {
                    throw new Exception($"{nameof(MonitoringBaseSettings.FromEmailName)} not found in settings");
                }

                var emailService = new EmailService(
                    sendGridKey.Value,
                    fromEmailName.Value,
                    fromEmailAddress.Value);

                // Get rules
                var caseId = _sdkCore.CaseIdLookup(message.microtingUId, message.checkUId) ?? 0;
                var replyElement = _sdkCore.CaseRead(message.microtingUId, message.checkUId);
                var checkListValue = (CheckListValue)replyElement.ElementList[0];
                var fields = checkListValue.DataItemList;

                var rules = await _dbContext.Rules
                    .AsNoTracking()
                    .Include(x => x.Recipients)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.CheckListId == message.checkListId)
                    .ToListAsync();

                // Find trigger
                foreach (var rule in rules)
                {
                    var dataItemId = rule.DataItemId;
                    var field = (Field)fields.FirstOrDefault(x => x.Id == dataItemId);

                    if (field != null)
                    {
                        // Check
                        var jsonSettings = new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Include
                        };
                        var sendEmail = false;
                        switch (field.FieldType)
                        {
                            case "Number":
                                var numberBlock = JsonConvert.DeserializeObject<NumberBlock>(rule.Data, jsonSettings);
                                var numberVal = int.Parse(field.FieldValues[0].Value);

                                sendEmail = true;

                                if (numberBlock.GreaterThanValue != null && numberVal < numberBlock.GreaterThanValue)
                                {
                                    sendEmail = false;
                                }

                                if (numberBlock.LessThanValue != null && numberVal > numberBlock.LessThanValue)
                                {
                                    sendEmail = false;
                                }

                                if (numberBlock.EqualValue != null && numberVal == numberBlock.EqualValue)
                                {
                                    sendEmail = true;
                                }

                                break;
                            case "CheckBox":
                                var checkboxBlock = JsonConvert.DeserializeObject<CheckBoxBlock>(rule.Data, jsonSettings);
                                var isChecked = field.FieldValue == "1" || field.FieldValue == "checked";
                                sendEmail = isChecked == checkboxBlock.Selected;
                                break;
                            case "MultiSelect":
                            case "SingleSelect":
                            case "EntitySearch":
                            case "EntitySelect":
                                var selectBlock = JsonConvert.DeserializeObject<SelectBlock>(rule.Data, jsonSettings);
                                var selectKeys = field.FieldValues[0].Value.Split('|');

                                sendEmail = selectBlock.KeyValuePairList.Any(i => i.Selected && selectKeys.Contains(i.Key));

                                break;
                        }

                        // Send email
                        if (sendEmail)
                        {
                            if (rule.AttachReport)
                            {
                                foreach (var recipient in rule.Recipients.Where(r => r.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    try
                                    {
                                        // Fix for broken SDK not handling empty customXmlContent well
                                        string customXmlContent = new XElement("FillerElement",
                                            new XElement("InnerElement", "SomeValue")).ToString();

                                        // get report file
                                        var filePath = _sdkCore.CaseToPdf(
                                            caseId,
                                            replyElement.Id.ToString(),
                                            DateTime.Now.ToString("yyyyMMddHHmmssffff"),
                                            $"{_sdkCore.GetSdkSetting(Settings.httpServerAddress)}/" + "api/template-files/get-image/",
                                            "pdf",
                                            customXmlContent);

                                        if (!System.IO.File.Exists(filePath))
                                        {
                                            throw new Exception("Error while creating report file");
                                        }

                                        await emailService.SendFileAsync(
                                            rule.Subject,
                                            recipient.Email,
                                            rule.Text,
                                            filePath);
                                    }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine(e);
                                        await emailService.SendAsync(
                                            rule.Subject,
                                            recipient.Email,
                                            rule.Text);
                                    }
                                }
                            }
                            else
                            {
                                foreach (var recipient in rule.Recipients)
                                {
                                    await emailService.SendAsync(
                                        rule.Subject,
                                        recipient.Email,
                                        rule.Text);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}