﻿using SmarthomeApi.FormatParsers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmarthomeApi.Database.Model
{
    public class ViessmannSolarData
    {
        private const int interval5min = 60 / 5 * 24;

        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime Id { get; set; }

        [Required]
        public DateTime Day { get; set; }

        public int? SolarWhTotal { get; set; }

        [MaxLength(800)]
        public string SolarWhCurve { get; set; }

        [MaxLength(800)]
        public string SolarCollectorTempCurve { get; set; }

        [MaxLength(800)]
        public string SolarHotwaterTempCurve { get; set; }

        [MaxLength(100)]
        public string SolarPumpStateCurve { get; set; }

        [MaxLength(100)]
        public string SolarSuppressionCurve { get; set; }

        [NotMapped]
        public TimeSeries<int> SolarWhSeries
        {
            get => SolarWhCurve.ToTimeseries<int>(Day, Day.AddDays(1), interval5min);
            set { SolarWhCurve = value.ToBase64(); }
        }

        [NotMapped]
        public TimeSeries<double> SolarCollectorTempSeries
        {
            get => SolarCollectorTempCurve.ToTimeseries<double>(Day, Day.AddDays(1), interval5min);
            set { SolarCollectorTempCurve = value.ToBase64(); }
        }

        [NotMapped]
        public TimeSeries<double> SolarHotwaterTempSeries
        {
            get => SolarHotwaterTempCurve.ToTimeseries<double>(Day, Day.AddDays(1), interval5min);
            set { SolarHotwaterTempCurve = value.ToBase64(); }
        }

        [NotMapped]
        public TimeSeries<bool> SolarPumpStateSeries
        {
            get => SolarPumpStateCurve.ToTimeseries<bool>(Day, Day.AddDays(1), interval5min);
            set { SolarPumpStateCurve = value.ToBase64(); }
        }

        [NotMapped]
        public TimeSeries<bool> SolarSuppressionSeries
        {
            get => SolarSuppressionCurve.ToTimeseries<bool>(Day, Day.AddDays(1), interval5min);
            set { SolarSuppressionCurve = value.ToBase64(); }
        }
    }
}
