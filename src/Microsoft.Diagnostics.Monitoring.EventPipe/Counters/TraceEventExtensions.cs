﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal static class TraceEventExtensions
    {
        public static bool TryGetCounterPayload(this TraceEvent traceEvent, CounterFilter filter, out ICounterPayload payload)
        {
            payload = null;

            if ("EventCounters".Equals(traceEvent.EventName))
            {
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(traceEvent.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                //Make sure we are part of the requested series. If multiple clients request metrics, all of them get the metrics.
                string series = payloadFields["Series"].ToString();
                string counterName = payloadFields["Name"].ToString();

                Dictionary<string, string> metadataDict = GetMetadata(payloadFields["Metadata"].ToString());

                //CONSIDER
                //Concurrent counter sessions do not each get a separate interval. Instead the payload
                //for _all_ the counters changes the Series to be the lowest specified interval, on a per provider basis.
                //Currently the CounterFilter will remove any data whose Series doesn't match the requested interval.
                if (!filter.IsIncluded(traceEvent.ProviderName, counterName, GetInterval(series)))
                {
                    return false;
                }

                float intervalSec = (float)payloadFields["IntervalSec"];
                string displayName = payloadFields["DisplayName"].ToString();
                string displayUnits = payloadFields["DisplayUnits"].ToString();
                double value = 0;
                CounterType counterType = CounterType.Metric;

                if (payloadFields["CounterType"].Equals("Mean"))
                {
                    value = (double)payloadFields["Mean"];
                }
                else if (payloadFields["CounterType"].Equals("Sum"))
                {
                    counterType = CounterType.Rate;
                    value = (double)payloadFields["Increment"];
                    if (string.IsNullOrEmpty(displayUnits))
                    {
                        displayUnits = "count";
                    }
                    //TODO Should we make these /sec like the dotnet-counters tool?
                }

                // Note that dimensional data such as pod and namespace are automatically added in prometheus and azure monitor scenarios.
                // We no longer added it here.
                payload = new CounterPayload(
                    traceEvent.TimeStamp,
                    traceEvent.ProviderName,
                    counterName, displayName,
                    displayUnits,
                    value,
                    counterType,
                    intervalSec,
                    metadataDict);
                return true;
            }

            return false;
        }

        //The metadata payload is formatted as a string of comma separated key:value pairs.
        //This limitation means that metadata values cannot include commas; otherwise, the
        //metadata will be parsed incorrectly. If a value contains a comma, then all metadata
        //is treated as invalid and excluded from the payload.
        internal static Dictionary<string, string> GetMetadata(string metadataPayload)
        {
            var metadataDict = new Dictionary<string, string>();

            ReadOnlySpan<char> metadata = metadataPayload;

            while (!metadata.IsEmpty)
            {
                int commaIndex = metadata.IndexOf(',');

                ReadOnlySpan<char> kvPair;

                if (commaIndex < 0)
                {
                    kvPair = metadata;
                    metadata = default;
                }
                else
                {
                    kvPair = metadata[..commaIndex];
                    metadata = metadata.Slice(commaIndex + 1);
                }

                int colonIndex = kvPair.IndexOf(':');
                if (colonIndex < 0)
                {
                    metadataDict.Clear();
                    break;
                }

                string metadataKey = kvPair[..colonIndex].ToString();
                string metadataValue = kvPair.Slice(colonIndex + 1).ToString();
                metadataDict[metadataKey] = metadataValue;
            }

            return metadataDict;
        }

        private static int GetInterval(string series)
        {
            const string comparison = "Interval=";
            int interval = 0;
            if (series.StartsWith(comparison, StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(series.Substring(comparison.Length), out interval);
            }
            return interval;
        }
    }
}
