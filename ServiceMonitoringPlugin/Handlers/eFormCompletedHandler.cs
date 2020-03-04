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
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
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
    using System.Globalization;
    using OpenStack.NetCoreSwiftClient.Extensions;

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
                var settings = await _dbContext.PluginConfigurationValues.ToListAsync();
                var sendGridKey = settings.FirstOrDefault(x =>
                    x.Name == nameof(MonitoringBaseSettings) + ":" + nameof(MonitoringBaseSettings.SendGridApiKey));
                if (sendGridKey == null)
                {
                    throw new Exception($"{nameof(MonitoringBaseSettings.SendGridApiKey)} not found in settings");
                }
                
                Log.LogEvent($"EFormCompletedHandler.Handle: sendGridKey is {sendGridKey.Value}");
                var fromEmailAddress = settings.FirstOrDefault(x =>
                    x.Name == nameof(MonitoringBaseSettings) + ":" + nameof(MonitoringBaseSettings.FromEmailAddress));
                if (fromEmailAddress == null)
                {
                    throw new Exception($"{nameof(MonitoringBaseSettings.FromEmailAddress)} not found in settings");
                }
                
                Log.LogEvent($"EFormCompletedHandler.Handle: fromEmailAddress is {fromEmailAddress.Value}");
                var fromEmailName = settings.FirstOrDefault(x =>
                    x.Name == nameof(MonitoringBaseSettings) + ":" + nameof(MonitoringBaseSettings.FromEmailName));
                if (fromEmailName == null)
                {
                    throw new Exception($"{nameof(MonitoringBaseSettings.FromEmailName)} not found in settings");
                }
                Log.LogEvent($"EFormCompletedHandler.Handle: fromEmailName is {fromEmailName.Value}");

                var emailService = new EmailService(sendGridKey.Value, fromEmailAddress.Value, fromEmailName.Value);

                // Get rules
                var caseId = await _sdkCore.CaseIdLookup(message.microtingUId, message.checkUId) ?? 0;
                var replyElement = await _sdkCore.CaseRead(message.microtingUId, message.checkUId);
                var checkListValue = (CheckListValue)replyElement.ElementList[0];
                var fields = checkListValue.DataItemList
                    .SelectMany(f => (f is FieldContainer fc) ? fc.DataItemList : new List<DataItem>() { f })
                    .ToList();

                var rules = await _dbContext.Rules
                    .Include(x => x.Recipients)
                    .Include(x => x.DeviceUsers)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.CheckListId == message.checkListId)
                    .ToListAsync();

                // Find trigger
                foreach (var rule in rules)
                {
                    if (rule.DataItemId == null)
                    {
                        continue;
                    }
                    var dataItemId = rule.DataItemId;
                    var field = (Field)fields.FirstOrDefault(x => x.Id == dataItemId);
                    // get device user who completed eform
                    var siteDto = await _sdkCore.SiteRead(replyElement.SiteMicrotingUuid);
                    // get list of device users in rule
                    var deviceUsersInRule = rule.DeviceUsers;
                    // if no device users in rule - run this rule
                    if (deviceUsersInRule.Any())
                    {
                        var deviceUsersInRuleIds = deviceUsersInRule
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(x => x.DeviceUserId).ToList();
                        // check if current user in rule
                        if (!deviceUsersInRuleIds.Contains(siteDto.SiteId))
                        {
                            continue;
                        }
                    }

                    if (field != null)
                    {
                        // Check
                        var jsonSettings = new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Include
                        };
                        var sendEmail = false;
                        var matchedValue = "";
                        switch (field.FieldType)
                        {
                            case "Number":
                                var numberBlock = JsonConvert.DeserializeObject<NumberBlock>(rule.Data, jsonSettings);

                                matchedValue = field.FieldValues[0].Value;
                                sendEmail = false;
                                if (float.TryParse(field.FieldValues[0].Value, NumberStyles.Any,
                                    CultureInfo.InvariantCulture, out var numberVal))
                                {
                                    if (numberBlock.GreaterThanValue != null &&
                                        numberVal > numberBlock.GreaterThanValue)
                                    {
                                        Log.LogEvent(
                                            $"EFormCompletedHandler.Handle: numberVal is {fromEmailName.Value} and is greater than {numberBlock.GreaterThanValue}");
                                        sendEmail = true;
                                    }

                                    if (numberBlock.LessThanValue != null && numberVal < numberBlock.LessThanValue)
                                    {
                                        Log.LogEvent(
                                            $"EFormCompletedHandler.Handle: numberVal is {fromEmailName.Value} and is less than {numberBlock.GreaterThanValue}");
                                        sendEmail = true;
                                    }

                                    if (numberBlock.EqualValue != null && numberVal.Equals((float)numberBlock.EqualValue))
                                    {
                                        Log.LogEvent(
                                            $"EFormCompletedHandler.Handle: numberVal is {fromEmailName.Value} and is equal to {numberBlock.GreaterThanValue}");
                                        sendEmail = true;
                                    }
                                }
                                break;
                            case "CheckBox":
                                var checkboxBlock = JsonConvert.DeserializeObject<CheckBoxBlock>(rule.Data, jsonSettings);
                                var isChecked = field.FieldValues[0].Value == "1" || field.FieldValues[0].Value == "checked";
                                matchedValue = checkboxBlock.Selected ? "Checked" : "Not checked";
                                sendEmail = isChecked == checkboxBlock.Selected;
                                break;
                            case "MultiSelect":
                            case "SingleSelect":
                                var selectBlock = JsonConvert.DeserializeObject<SelectBlock>(rule.Data, jsonSettings);
                                var selectKeys = field.FieldValues[0].Value.Split('|');

                                matchedValue = field.FieldValues[0].ValueReadable;
                                sendEmail = selectBlock.KeyValuePairList.Any(i => i.Selected && selectKeys.Contains(i.Key));
                                break;
                            case "EntitySearch":
                            case "EntitySelect":
                                var entityBlock = JsonConvert.DeserializeObject<SelectBlock>(rule.Data, jsonSettings);
                                var selectedId = field.FieldValues[0].Value;
                                
                                matchedValue = field.FieldValues[0].ValueReadable;
                                sendEmail = entityBlock.KeyValuePairList.Any(i => i.Selected && selectedId == i.Key);
                                break;
                        }

                        // Send email
                        if (sendEmail)
                        {
                            Log.LogEvent($"EFormCompletedHandler.Handle: sendmail is true, so let's send an email");
                            var assembly = Assembly.GetExecutingAssembly();
                            var assemblyName = assembly.GetName().Name;
                            var stream = assembly.GetManifestResourceStream($"{assemblyName}.Resources.Email.html");
                            string html;

                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                html = await reader.ReadToEndAsync();
                            }

                            html = html.Replace("{{label}}", field.Label)
                                .Replace("{{description}}", field.Description.InderValue)
                                .Replace("{{value}}", matchedValue)
                                .Replace("{{link}}", $"{await _sdkCore.GetSdkSetting(Settings.httpServerAddress)}/cases/edit/{caseId}/{message.checkListId}")
                                .Replace("{{text}}", rule.Text);

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
                                        var filePath = await _sdkCore.CaseToPdf(
                                            caseId,
                                            replyElement.Id.ToString(),
                                            DateTime.Now.ToString("yyyyMMddHHmmssffff"),
                                            $"{await _sdkCore.GetSdkSetting(Settings.httpServerAddress)}/" + "api/template-files/get-image/",
                                            "pdf",
                                            customXmlContent);

                                        if (!File.Exists(filePath))
                                        {
                                            throw new Exception("Error while creating report file");
                                        }

                                        await emailService.SendFileAsync(
                                            rule.Subject.IsNullOrEmpty() ? "-" : rule.Subject,
                                            recipient.Email,
                                            filePath,
                                            html: html);
                                    }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine(e);
                                        await emailService.SendAsync(
                                            rule.Subject.IsNullOrEmpty() ? "-" : rule.Subject,
                                            recipient.Email,
                                            html: html);
                                    }
                                }
                            }
                            else
                            {
                                foreach (var recipient in rule.Recipients)
                                {
                                    await emailService.SendAsync(
                                        rule.Subject.IsNullOrEmpty() ? "-" : rule.Subject,
                                        recipient.Email,
                                        html: html);
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