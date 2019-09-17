﻿using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NodaTime;
using PhilipDaubmeier.CompactTimeSeries;
using PhilipDaubmeier.TimeseriesHostCommon.Parsers;
using PhilipDaubmeier.TimeseriesHostCommon.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PhilipDaubmeier.GrafanaHost.Controllers
{
    /// <summary>
    /// More documentation about datasource plugins can be found in the Docs:
    /// https://github.com/grafana/grafana/blob/master/docs/sources/plugins/developing/datasources.md
    /// 
    /// A grafana json data source backend needs to implement 4 urls:
    /// "/" should return 200 ok.Used for "Test connection" on the datasource config page.
    /// "/search" used by the find metric options on the query tab in panels.
    /// "/query" should return metrics based on input.
    /// "/annotations" should return annotations.
    /// 
    /// Those two urls are optional:
    /// 
    /// "/tag-keys" should return tag keys for ad hoc filters.
    /// "/tag-values" should return tag values for ad hoc filters.
    /// </summary>
    [Produces("application/json")]
    [Route("api/grafana")]
    public class GrafanaController : Controller
    {
        private List<string> graphIds = null;
        public List<string> GraphIds
        {
            get
            {
                string ToRawId(string name) => Regex.Replace(name.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe")
                    .Replace("ü", "ue").Replace("ß", "ss"), @"[^\u0000-\u007F]+", string.Empty).Replace(' ', '_');

                if (graphIds == null)
                {
                    graphIds = graphViewModels
                        .SelectMany(n => n.Value.Graphs()
                            .Zip(Enumerable.Range(0, 100), (g, i) => new Tuple<int, string, string>(i, g.Name, g.Key))
                            .SelectMany(t=>new string[]
                            {
                                t.Item2,
                                $"{n.Key}_{t.Item1}_{ToRawId(t.Item3)}"
                            })
                        ).ToList();
                }
                return graphIds;
            }
        }

        private readonly Dictionary<string, IGraphCollectionViewModel> graphViewModels;
        private readonly Dictionary<string, IEventCollectionViewModel> eventViewModels;

        public GrafanaController(IEnumerable<IGraphCollectionViewModel> graphViewModels, IEnumerable<IEventCollectionViewModel> eventViewModels)
        {
            this.graphViewModels = graphViewModels.ToDictionary(x => x.Key, x => x);
            this.eventViewModels = eventViewModels.ToDictionary(x => x.Key, x => x);
        }

        // GET: api/grafana/
        [HttpGet]
        public ActionResult TestConnection()
        {
            return StatusCode((int)HttpStatusCode.OK);
        }

        // POST: api/grafana/search
        [HttpPost("search")]
        public ActionResult Search()
        {
            return Json(GraphIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        }

        // POST: api/grafana/query
        [HttpPost("query")]
        public async Task<ActionResult> Query()
        {
            var definition = new
            {
                timezone = "",
                panelId = (int?)0,
                dashboardId = (int?)0,
                range = new
                {
                    from = "",
                    to = "",
                    raw = new
                    {
                        from = "",
                        to = ""
                    }
                },
                rangeRaw = new
                {
                    from = "",
                    to = ""
                },
                interval = "",
                intervalMs = 0L,
                targets = new[] { new
                {
                    data = new
                    {
                        rawMetricId = "",
                        filterIfNoneOf = new List<string>(),
                        aggregate = new
                        {
                            interval = "",
                            func = ""
                        },
                        correction = new
                        {
                            factor = 0d,
                            offset = 0d
                        },
                        overrideMaxDataPoints = (int?)0
                    },
                    target = "",
                    refId = "",
                    type = "timeserie"
                } },
                adhocFilters = new[] { new
                {
                    key = "",
                    @operator = "=",
                    value = ""
                } },
                format = "json",
                maxDataPoints = 0
            };

            string body = string.Empty;
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            var query = JsonConvert.DeserializeAnonymousType(body, definition);

            if (!DateTime.TryParse(query.range.from, out DateTime fromDate) || !DateTime.TryParse(query.range.to, out DateTime toDate))
                return StatusCode((int)HttpStatusCode.NotFound);

            var span = new TimeSeriesSpan(fromDate.ToUniversalTime(), toDate.ToUniversalTime(), query.maxDataPoints);

            var data = new Dictionary<string, List<dynamic[]>>();
            foreach (var target in query.targets)
            {
                var targetId = target?.data?.rawMetricId ?? target?.target;
                if (string.IsNullOrEmpty(targetId))
                    continue;

                // filter if the filterIfNoneOf list is present but the target is not contained
                var filterIfNoneOf = target?.data?.filterIfNoneOf;
                if (filterIfNoneOf != null && !filterIfNoneOf.Contains(targetId))
                    continue;

                // find the parent viewmodel of the target graph
                var splitted = targetId.Split('_');
                if (splitted.Length < 2 || !int.TryParse(splitted[1], out int index) || index < 0
                    || !graphViewModels.ContainsKey(splitted[0]) || index >= graphViewModels[splitted[0]].GraphCount())
                    continue;
                var viewModel = graphViewModels[splitted[0]];

                // if a custom 'aggregate.interval' or 'overrideMaxDataPoints' was given, take that as time spacing
                var targetSpan = span;
                var spanOverride = target?.data?.aggregate?.interval?.ToTimeSpan();
                if (spanOverride.HasValue && spanOverride.Value > span.Duration)
                    targetSpan = new TimeSeriesSpan(fromDate, toDate, spanOverride.Value);
                else if (target?.data?.overrideMaxDataPoints.HasValue ?? false)
                    targetSpan = new TimeSeriesSpan(fromDate, toDate, target.data.overrideMaxDataPoints.Value);
                viewModel.Span = targetSpan;

                // if a custom 'aggregate.func' was given, take that as aggregation method
                Aggregator ToAggregator(string aggregateRaw)
                {
                    switch (aggregateRaw?.Substring(0, Math.Min(3, target.data.aggregate.func.Length))?.ToLowerInvariant())
                    {
                        case "min": return Aggregator.Minimum;
                        case "max": return Aggregator.Maximum;
                        case "avg": return Aggregator.Average;
                        case "sum": return Aggregator.Sum;
                        default: return Aggregator.Default;
                    }
                }
                viewModel.AggregatorFunction = ToAggregator(target?.data?.aggregate?.func);

                // if a custom correction factor and/or offset was given, set them to get them taken into account
                viewModel.CorrectionFactor = ((decimal?)target?.data?.correction?.factor) ?? 1M;
                viewModel.CorrectionOffset = ((decimal?)target?.data?.correction?.offset) ?? 0M;

                // add the resampled target graph to the result
                data.Add(target?.target ?? targetId, viewModel.Graph(index).TimestampedPoints().ToList());
            }

            return Json(data.Select(d => new { target = d.Key, datapoints = d.Value }));
        }

        // POST: api/grafana/annotations
        [HttpPost("annotations")]
        public async Task<ActionResult> AnnotationsAsync()
        {
            var definition = new
            {
                range = new
                {
                    from = "",
                    to = ""
                },
                rangeRaw = new
                {
                    from = "",
                    to = ""
                },
                annotation = new
                {
                    name = "",
                    datasource = "",
                    iconColor = "",
                    enable = true,
                    query = ""
                }
            };

            string body = string.Empty;
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            var annotationInfo = JsonConvert.DeserializeAnonymousType(body, definition);

            if (!DateTime.TryParse(annotationInfo.range.from, out DateTime fromDate) || !DateTime.TryParse(annotationInfo.range.to, out DateTime toDate))
                return StatusCode((int)HttpStatusCode.NotFound);

            var eventViewModel = eventViewModels.FirstOrDefault().Value;
            eventViewModel.Span = new TimeSeriesSpan(fromDate, toDate, 1);
            eventViewModel.Query = annotationInfo.annotation.query;

            return Json(eventViewModel.Events.Select(item => new
            {
                annotation = annotationInfo.annotation.name,
                time = Instant.FromDateTimeUtc(item.Time.ToUniversalTime()).ToUnixTimeMilliseconds(),
                title = item.Title,
                tags = item.Tags,
                text = item.Text
            }));
        }
    }
}