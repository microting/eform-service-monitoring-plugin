namespace ServiceMonitoringPlugin.Messages
{
    public class eFormRetrieved
    {
        public string caseId { get; protected set; }

        public eFormRetrieved(string caseId)
        {
            this.caseId = caseId;
        }
    }
}