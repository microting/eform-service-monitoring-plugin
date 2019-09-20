using System.Linq;
using System.Threading.Tasks;
using Microting.MonitoringBase.Infrastructure.Data;
using Microting.MonitoringBase.Infrastructure.Data.Entities;
using Rebus.Handlers;
using ServiceMonitoringPlugin.Messages;

namespace ServiceMonitoringPlugin.Handlers
{
    public class EFormRetrievedHandler : IHandleMessages<eFormRetrieved>
    {
        private readonly eFormCore.Core _sdkCore;
        private readonly MonitoringPnDbContext _dbContext;

        public EFormRetrievedHandler(eFormCore.Core sdkCore, MonitoringPnDbContext dbContext)
        {
            _dbContext = dbContext;
            _sdkCore = sdkCore;
        }

        public async Task Handle(eFormRetrieved message)
        {
        }
    }
}