﻿using PhilipDaubmeier.WeConnectClient.Model.Emanager;
using PhilipDaubmeier.WeConnectClient.Model.TripStatistics;
using PhilipDaubmeier.WeConnectClient.Network;
using System.Threading.Tasks;

namespace PhilipDaubmeier.WeConnectClient
{
    public class WeConnectPortalClient : WeConnectAuthBase
    {
        public WeConnectPortalClient(IWeConnectConnectionProvider connectionProvider)
            : base(connectionProvider) { }

        public async Task<Emanager> GetEManager()
        {
            return await CallApi<EmanagerResponse, Emanager>("/-/emanager/get-emanager");
        }

        public async Task<Rts> GetTripStatistics()
        {
            return await CallApi<TripStatisticsResponse, Rts>("/-/rts/get-latest-trip-statistics");
        }
    }
}