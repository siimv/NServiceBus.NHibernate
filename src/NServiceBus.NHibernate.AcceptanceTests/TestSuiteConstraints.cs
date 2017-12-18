﻿namespace NServiceBus.AcceptanceTests
{
    using AcceptanceTesting.Support;

    public partial class TestSuiteConstraints
    {
        public bool SupportsDtc => false;
        public bool SupportsCrossQueueTransactions => true;
        public bool SupportsNativePubSub => false;
        public bool SupportsNativeDeferral => false;
        public bool SupportsOutbox => true;
        public IConfigureEndpointTestExecution CreateTransportConfiguration() => new ConfigureAcceptanceTestingTransport(false, false);
        public IConfigureEndpointTestExecution CreatePersistenceConfiguration() => new ConfigureEndpointNHibernatePersistence();
    }
}