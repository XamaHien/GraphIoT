﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhilipDaubmeier.CompactTimeSeries;
using PhilipDaubmeier.GraphIoT.Viessmann.Config;
using PhilipDaubmeier.GraphIoT.Viessmann.Database;
using PhilipDaubmeier.ViessmannClient;
using PhilipDaubmeier.ViessmannClient.Model.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhilipDaubmeier.GraphIoT.Viessmann.Polling
{
    public class ViessmannHeatingPollingService : IViessmannPollingService
    {
        private readonly ILogger _logger;
        private readonly IViessmannDbContext _dbContext;
        private readonly ViessmannConfig _config;
        private readonly ViessmannPlatformClient _platformClient;

        public ViessmannHeatingPollingService(ILogger<ViessmannHeatingPollingService> logger, IViessmannDbContext dbContext, IOptions<ViessmannConfig> config, ViessmannPlatformClient platformClient)
        {
            _logger = logger;
            _dbContext = dbContext;
            _config = config.Value;
            _platformClient = platformClient;
        }

        public async Task PollValues()
        {
            _logger.LogInformation($"{DateTime.Now} Viessmann Background Service is polling new heating values...");

            try
            {
                await PollHeatingValues();
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"{DateTime.Now} Exception occurred in viessmann heating background worker: {ex.Message}");
            }
        }

        private async Task PollHeatingValues()
        {
            var features = await _platformClient.GetDeviceFeatures(_config.InstallationId, _config.GatewayId);

            var time = features.GetTimestamp();

            var burnerHoursTotal = (double)features.GetHeatingBurnerStatisticsHours();
            var burnerStartsTotal = (int)features.GetHeatingBurnerStatisticsStarts();
            var burnerModulation = features.GetHeatingBurnerModulation();

            var outsideTemp = features.GetHeatingSensorsTemperatureOutside();
            var boilerTemp = features.GetHeatingBoilerTemperature();
            var boilerTempMain = features.GetHeatingBoilerSensorsTemperatureMain();
            var circuit0Temp = features.GetHeatingCircuitsSensorsTemperature(FeatureName.Circuit.Circuit0);
            var circuit1Temp = features.GetHeatingCircuitsSensorsTemperature(FeatureName.Circuit.Circuit1);
            var dhwTemp = features.GetHeatingDhwSensorsTemperatureHotWaterStorage();

            var burnerActive = features.IsHeatingBurnerActive();
            var circuit0Pump = features.IsHeatingCircuitsCirculationPumpOn(FeatureName.Circuit.Circuit0);
            var circuit1Pump = features.IsHeatingCircuitsCirculationPumpOn(FeatureName.Circuit.Circuit1);
            var dhwPrimPump = features.IsHeatingDhwPumpsPrimaryOn();
            var dhwCircPump = features.IsHeatingDhwPumpsCirculationOn();

            var solarWhTotal = features.GetHeatingSolarPowerProductionWhToday();
            var solarCollectorTemp = features.GetHeatingSolarSensorsTemperatureCollector();
            var solarHotwaterTemp = features.GetHeatingSolarSensorsTemperatureDhw();
            var solarPumpState = features.IsHeatingSolarPumpsCircuitOn();
            var solarSuppression = features.IsHeatingSolarRechargeSuppressionOn();

            SaveHeatingValues(time, burnerHoursTotal, burnerStartsTotal, burnerModulation, outsideTemp, boilerTemp, boilerTempMain, circuit0Temp, circuit1Temp, dhwTemp, burnerActive, circuit0Pump, circuit1Pump, dhwPrimPump, dhwCircPump);

            ViessmannSolarPollingService.SaveSolarValues(_dbContext, time, solarWhTotal, solarCollectorTemp, solarHotwaterTemp, solarPumpState, solarSuppression);
        }

        private void SaveHeatingValues(DateTime time, double burnerHoursTotal, int burnerStartsTotal, int burnerModulation, double outsideTemp, double boilerTemp, double boilerTempMain, double circuit0Temp, double circuit1Temp, double dhwTemp, bool burnerActive, bool circuit0Pump, bool circuit1Pump, bool dhwPrimPump, bool dhwCircPump)
        {
            var day = time.Date;
            var dbHeatingSeries = _dbContext.ViessmannHeatingTimeseries.Where(x => x.Key == day).FirstOrDefault();
            if (dbHeatingSeries == null)
                _dbContext.ViessmannHeatingTimeseries.Add(dbHeatingSeries = new ViessmannHeatingMidresData() { Key = day, BurnerHoursTotal = 0d, BurnerStartsTotal = 0 });

            var oldHours = dbHeatingSeries.BurnerHoursTotal;
            var minutes = (burnerHoursTotal - oldHours) * 60;
            var series1 = dbHeatingSeries.BurnerMinutesSeries;
            series1.Accumulate(time, minutes > 10 || minutes < 0 ? 0 : minutes);
            dbHeatingSeries.SetSeries(0, series1);
            dbHeatingSeries.BurnerHoursTotal = burnerHoursTotal;

            var oldStarts = dbHeatingSeries.BurnerStartsTotal;
            var startsDiff = burnerStartsTotal - oldStarts;
            var series2 = dbHeatingSeries.BurnerStartsSeries;
            series1.Accumulate(time, startsDiff > 10 || startsDiff < 0 ? 0 : startsDiff);
            dbHeatingSeries.SetSeries(1, series2);
            dbHeatingSeries.BurnerStartsTotal = burnerStartsTotal;

            dbHeatingSeries.SetSeriesValue(2, time, burnerModulation);
            dbHeatingSeries.SetSeriesValue(3, time, outsideTemp);
            dbHeatingSeries.SetSeriesValue(4, time, boilerTemp);
            dbHeatingSeries.SetSeriesValue(5, time, boilerTempMain);
            dbHeatingSeries.SetSeriesValue(6, time, circuit0Temp);
            dbHeatingSeries.SetSeriesValue(7, time, circuit1Temp);
            dbHeatingSeries.SetSeriesValue(8, time, dhwTemp);
            dbHeatingSeries.SetSeriesValue(9, time, burnerActive);
            dbHeatingSeries.SetSeriesValue(10, time, circuit0Pump);
            dbHeatingSeries.SetSeriesValue(11, time, circuit1Pump);
            dbHeatingSeries.SetSeriesValue(12, time, dhwPrimPump);
            dbHeatingSeries.SetSeriesValue(13, time, dhwCircPump);

            SaveLowresHeatingValues(day, dbHeatingSeries);

            _dbContext.SaveChanges();
        }

        public void GenerateLowResHeatingSeries(DateTime start, DateTime end)
        {
            foreach (var day in new TimeSeriesSpan(start, end, 1).IncludedDates())
            {
                var dbHeatingSeries = _dbContext.ViessmannHeatingTimeseries.Where(x => x.Key == day).FirstOrDefault();
                if (dbHeatingSeries == null)
                    continue;

                SaveLowresHeatingValues(day, dbHeatingSeries);
                _dbContext.SaveChanges();
            }
        }

        private void SaveLowresHeatingValues(DateTime day, ViessmannHeatingMidresData midRes)
        {
            static DateTime FirstOfMonth(DateTime date) => date.AddDays(-1 * (date.Day - 1));
            var month = FirstOfMonth(day);
            var dbHeatingSeries = _dbContext.ViessmannHeatingLowresTimeseries.Where(x => x.Key == month).FirstOrDefault();
            if (dbHeatingSeries == null)
                _dbContext.ViessmannHeatingLowresTimeseries.Add(dbHeatingSeries = new ViessmannHeatingLowresData() { Key = month });

            dbHeatingSeries.ResampleFrom<double>(midRes, 0, x => x.Average());
            dbHeatingSeries.ResampleFrom<int>(midRes, 1, x => (int)x.Average());
            dbHeatingSeries.ResampleFrom<int>(midRes, 2, x => (int)x.Average());
            dbHeatingSeries.ResampleFrom<double>(midRes, 3, x => x.Average());
            dbHeatingSeries.ResampleFrom<double>(midRes, 4, x => x.Average());
            dbHeatingSeries.ResampleFrom<double>(midRes, 5, x => x.Average());
            dbHeatingSeries.ResampleFrom<double>(midRes, 6, x => x.Average());
            dbHeatingSeries.ResampleFrom<double>(midRes, 7, x => x.Average());
            dbHeatingSeries.ResampleFrom<double>(midRes, 8, x => x.Average());
            dbHeatingSeries.ResampleFrom<bool>(midRes, 9, x => x.Any(v => v));
            dbHeatingSeries.ResampleFrom<bool>(midRes, 10, x => x.Any(v => v));
            dbHeatingSeries.ResampleFrom<bool>(midRes, 11, x => x.Any(v => v));
            dbHeatingSeries.ResampleFrom<bool>(midRes, 12, x => x.Any(v => v));
            dbHeatingSeries.ResampleFrom<bool>(midRes, 13, x => x.Any(v => v));
        }
    }
}