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
    using Helpers;
    using Messages;
    using Microsoft.EntityFrameworkCore;
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
                var templateId = 1;
                var rules = await _dbContext.Rules
                    .AsNoTracking()
                    .Include(x => x.Recipients)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.TemplateId == templateId)
                    .ToListAsync();

                var caseDto = _sdkCore.CaseLookupCaseId(message.CaseId);
                var replyElement = _sdkCore.CaseRead(caseDto.MicrotingUId, caseDto.CheckUId);
                var dataItems = replyElement.DataItemGetAll();

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
                                    // TODO Get report file
                                    string fileName = "";
                                    await emailService.SendFileAsync(
                                        rule.Subject,
                                        recipient.Email,
                                        rule.Text,
                                        fileName);
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