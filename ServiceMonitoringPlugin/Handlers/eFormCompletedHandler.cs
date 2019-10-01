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
                    x.Name == nameof(MonitoringBaseSettings.SendGridApiKey));
                if (sendGridKey == null)
                {
                    throw new Exception($"{nameof(MonitoringBaseSettings.SendGridApiKey)} not found in settings");
                }
                var fromEmailAddress = settings.FirstOrDefault(x => 
                    x.Name == nameof(MonitoringBaseSettings.FromEmailAddress));
                if (fromEmailAddress == null)
                {
                    throw new Exception($"{nameof(MonitoringBaseSettings.FromEmailAddress)} not found in settings");
                }
                var fromEmailName = settings.FirstOrDefault(x => 
                    x.Name == nameof(MonitoringBaseSettings.FromEmailName));
                if (fromEmailName == null)
                {
                    throw new Exception($"{nameof(MonitoringBaseSettings.FromEmailName)} not found in settings");
                }

                var emailService = new EmailService(
                    sendGridKey.Value,
                    fromEmailName.Value,
                    fromEmailAddress.Value);
                
                // Get rules
                var caseDto = _sdkCore.CaseLookupCaseId(message.CaseId);
                var replyElement = _sdkCore.CaseRead(caseDto.MicrotingUId, caseDto.CheckUId);
                var dataItems = replyElement.DataItemGetAll();
                var checkListId = caseDto.CheckListId;

                var rules = await _dbContext.Rules
                    .AsNoTracking()
                    .Include(x => x.Recipients)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.CheckListId == checkListId)
                    .ToListAsync();

                // Find trigger
                foreach (var rule in rules)
                {
                    var dataItemId = rule.DataItemId;
                    var dataItem = dataItems.FirstOrDefault(x => x.Id == dataItemId);
                    if (dataItem != null)
                    {
                        // Check
                        var sendEmail = false;
                        switch (dataItem)
                        {
                            case CheckBox checkBox:
                                var block = JsonConvert.DeserializeObject<CheckBoxBlock>(rule.Data);
                                if (checkBox.Selected == block.Selected)
                                {
                                    sendEmail = true;
                                }
                                break;
                            case EntitySearch entitySearch:
                                break;
                            case EntitySelect entitySelect:
                                break;
                            case MultiSelect multiSelect:
                                break;
                            case Number number:
                                break;
                            case SingleSelect singleSelect:
                                break;
                        }

                        // Send email
                        if (sendEmail)
                        {
                            if (rule.AttachReport)
                            {
                                foreach (var recipient in rule.Recipients)
                                {
                                    // Fix for broken SDK not handling empty customXmlContent well
                                    string customXmlContent = new XElement("FillerElement",
                                        new XElement("InnerElement", "SomeValue")).ToString();

                                    // get report file
                                    var filePath = _sdkCore.CaseToPdf(
                                        message.CaseId,
                                        checkListId.ToString(),
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