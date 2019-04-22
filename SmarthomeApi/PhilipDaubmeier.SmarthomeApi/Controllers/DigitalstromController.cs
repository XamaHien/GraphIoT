﻿using PhilipDaubmeier.CompactTimeSeries;
using PhilipDaubmeier.DigitalstromClient.Model;
using PhilipDaubmeier.DigitalstromClient.Network;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using PhilipDaubmeier.SmarthomeApi.Database.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PhilipDaubmeier.SmarthomeApi.Controllers
{
    [Route("api/digitalstrom")]
    public class DigitalstromController : Controller
    {
        private static DigitalstromWebserviceClient dsClient;

        private readonly PersistenceContext db;
        public DigitalstromController(PersistenceContext databaseContext, IDigitalstromConnectionProvider connectionProvider)
        {
            db = databaseContext;
            dsClient = new DigitalstromWebserviceClient(connectionProvider);
        }

        // GET api/digitalstrom/sensors
        [HttpGet("sensors")]
        public async Task<JsonResult> GetSensors()
        {
            var sensorValues = (await dsClient.GetZonesAndSensorValues()).zones;

            return Json(new
            {
                zones = sensorValues.Select(x => new Tuple<int, List<double>, List<double>>(
                        x.ZoneID,
                        x?.sensor?.Where(s => s.type == 9).Select(s => s.value).ToList(),
                        x?.sensor?.Where(s => s.type == 13).Select(s => s.value).ToList()
                    ))
                    .Where(x => (x.Item2?.Count ?? 0) > 0 || (x.Item3?.Count ?? 0) > 0)
                    .Select(x => new
                    {
                        zone_id = x.Item1,
                        temperature = (x.Item2?.Count ?? 0) > 0 ? (double?)x.Item2.First() : null,
                        humidity = (x.Item3?.Count ?? 0) > 0 ? (double?)x.Item3.First() : null
                    })
            });
        }

        // GET: api/digitalstrom/sensors/curves/days/{day}
        [HttpGet("sensors/curves/days/{day}")]
        public ActionResult GetSensorsCurves([FromRoute] string day)
        {
            if (!DateTime.TryParseExact(day, "yyyy'-'MM'-'dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime dayDate))
                return StatusCode(404);

            var sensorData = db.DsSensorDataSet.Where(x => x.Day == dayDate.Date).ToList();
            if (sensorData == null)
                return StatusCode(404);

            return Json(new
            {
                zones = sensorData.Select(zone => new
                {
                    id = zone.ZoneId,
                    temperature_curve = zone.TemperatureSeries.Trimmed(0d),
                    humidity_curve = zone.HumiditySeries.Trimmed(0d)
                })
            });
        }

        // GET: api/digitalstrom/energy/curves/days/{day}
        [HttpGet("energy/curves/days/{day}")]
        public async Task<ActionResult> GetEnergyCurves([FromRoute] string day)
        {
            if (!DateTime.TryParseExact(day, "yyyy'-'MM'-'dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime dayDate))
                return StatusCode(404);
            
            var energyData = db.DsEnergyHighresDataSet.Where(x => x.Day == dayDate.Date).FirstOrDefault();
            if (energyData == null)
                return StatusCode(404);

            var circuitNames = (await dsClient.GetMeteringCircuits()).FilteredMeterNames;

            Func<ITimeSeries<int>, DateTime> getBegin = ts => ts.SkipWhile(t => !t.Value.HasValue).FirstOrDefault().Key;

            return Json(new
            {
                circuits = energyData.EnergySeriesEveryMeter.Select(circuit => new
                {
                    dsuid = circuit.Key.ToString(),
                    name = circuitNames.ContainsKey(circuit.Key) ? circuitNames[circuit.Key] : null,
                    begin = Instant.FromDateTimeUtc(getBegin(circuit.Value).ToUniversalTime()).ToUnixTimeSeconds(),
                    energy_curve = circuit.Value.Trimmed(-1)
                })
            });
        }

        // GET: api/digitalstrom/events/callscene/days/{day}
        [HttpGet("events/callscene/days/{day}")]
        public ActionResult GetCallSceneEvents([FromRoute] string day)
        {
            if (!DateTime.TryParseExact(day, "yyyy'-'MM'-'dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime dayDate))
                return StatusCode(404);

            var eventData = db.DsSceneEventDataSet.Where(x => x.Day == dayDate.Date).FirstOrDefault();
            if (eventData == null)
                return StatusCode(404);
            
            return Json(new
            {
                events = eventData.EventStream.Select(dssEvent => new
                {
                    timestamp = dssEvent.TimestampUtc,
                    dssEvent.systemEvent.name,
                    zone = (int)dssEvent.properties.zone,
                    group = (int)dssEvent.properties.group,
                    scene = (int)dssEvent.properties.scene
                })
            });
        }
    }
}