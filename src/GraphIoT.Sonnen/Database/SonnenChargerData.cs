﻿using PhilipDaubmeier.CompactTimeSeries;
using PhilipDaubmeier.GraphIoT.Core.Database;
using PhilipDaubmeier.GraphIoT.Core.Parsers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PhilipDaubmeier.GraphIoT.Sonnen.Database
{
    public class SonnenChargerLowresData : SonnenChargerData
    {
        public override TimeSeriesSpan Span => SpanMonth160Min;

        [Required, Column("Month")]
        public override DateTime Key { get; set; }
    }

    public class SonnenChargerMidresData : SonnenChargerData
    {
        public override TimeSeriesSpan Span => SpanDay5Min;

        [Required, Column("Day")]
        public override DateTime Key { get; set; }

        public double? ChargedEnergyTotal { get; set; }
    }

    public abstract class SonnenChargerData : TimeSeriesDbEntityBase
    {
        protected override int DecimalPlaces => 1;

        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        [MaxLength(800)]
        [TimeSeries(typeof(double))]
        public string? ChargedEnergyCurve { get; set; }

        [MaxLength(800)]
        [TimeSeries(typeof(double))]
        public string? ActivePowerCurve { get; set; }

        [MaxLength(800)]
        [TimeSeries(typeof(double))]
        public string? CurrentCurve { get; set; }

        [MaxLength(100)]
        [TimeSeries(typeof(bool))]
        public string? ConnectedCurve { get; set; }

        [MaxLength(100)]
        [TimeSeries(typeof(bool))]
        public string? ChargingCurve { get; set; }

        [MaxLength(100)]
        [TimeSeries(typeof(bool))]
        public string? SmartModeCurve { get; set; }

        [NotMapped]
        public TimeSeries<double> ChargedEnergySeries => ChargedEnergyCurve.ToTimeseries<double>(Span, DecimalPlaces);
    }
}