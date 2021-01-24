using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Net.Http;
using Rainmeter;
using System.Net.Http.Headers;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Collections;
using System.Text;

namespace PluginParentChild
{

    // Src: https://stackoverflow.com/questions/3142495/deserialize-json-into-c-sharp-dynamic-object
    // Else we would need Newtonsoft Json which would add an additional dll as dependency for the runtime
    public sealed class DynamicJsonConverter : JavaScriptConverter
    {
        public override object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer)
        {
            if (dictionary == null)
                throw new ArgumentNullException("dictionary");

            return type == typeof(object) ? new DynamicJsonObject(dictionary) : null;
        }

        public override IDictionary<string, object> Serialize(object obj, JavaScriptSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<Type> SupportedTypes
        {
            get { return new ReadOnlyCollection<Type>(new List<Type>(new[] { typeof(object) })); }
        }

        #region Nested type: DynamicJsonObject

        private sealed class DynamicJsonObject : DynamicObject
        {
            private readonly IDictionary<string, object> _dictionary;

            public DynamicJsonObject(IDictionary<string, object> dictionary)
            {
                if (dictionary == null)
                    throw new ArgumentNullException("dictionary");
                _dictionary = dictionary;
            }

            public override string ToString()
            {
                var sb = new StringBuilder("{");
                ToString(sb);
                return sb.ToString();
            }

            private void ToString(StringBuilder sb)
            {
                var firstInDictionary = true;
                foreach (var pair in _dictionary)
                {
                    if (!firstInDictionary)
                        sb.Append(",");
                    firstInDictionary = false;
                    var value = pair.Value;
                    var name = pair.Key;
                    if (value is string)
                    {
                        sb.AppendFormat("{0}:\"{1}\"", name, value);
                    }
                    else if (value is IDictionary<string, object>)
                    {
                        new DynamicJsonObject((IDictionary<string, object>)value).ToString(sb);
                    }
                    else if (value is ArrayList)
                    {
                        sb.Append(name + ":[");
                        var firstInArray = true;
                        foreach (var arrayValue in (ArrayList)value)
                        {
                            if (!firstInArray)
                                sb.Append(",");
                            firstInArray = false;
                            if (arrayValue is IDictionary<string, object>)
                                new DynamicJsonObject((IDictionary<string, object>)arrayValue).ToString(sb);
                            else if (arrayValue is string)
                                sb.AppendFormat("\"{0}\"", arrayValue);
                            else
                                sb.AppendFormat("{0}", arrayValue);

                        }
                        sb.Append("]");
                    }
                    else
                    {
                        sb.AppendFormat("{0}:{1}", name, value);
                    }
                }
                sb.Append("}");
            }

            public override bool TryGetMember(GetMemberBinder binder, out object result)
            {
                if (!_dictionary.TryGetValue(binder.Name, out result))
                {
                    // return null to avoid exception.  caller can check for null this way...
                    result = null;
                    return true;
                }

                result = WrapResultObject(result);
                return true;
            }

            public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
            {
                if (indexes.Length == 1 && indexes[0] != null)
                {
                    if (!_dictionary.TryGetValue(indexes[0].ToString(), out result))
                    {
                        // return null to avoid exception.  caller can check for null this way...
                        result = null;
                        return true;
                    }

                    result = WrapResultObject(result);
                    return true;
                }

                return base.TryGetIndex(binder, indexes, out result);
            }

            private static object WrapResultObject(object result)
            {
                var dictionary = result as IDictionary<string, object>;
                if (dictionary != null)
                    return new DynamicJsonObject(dictionary);

                var arrayList = result as ArrayList;
                if (arrayList != null && arrayList.Count > 0)
                {
                    return arrayList[0] is IDictionary<string, object>
                        ? new List<object>(arrayList.Cast<IDictionary<string, object>>().Select(x => new DynamicJsonObject(x)))
                        : new List<object>(arrayList.Cast<object>());
                }

                return result;
            }
        }

        #endregion
    }

    internal class GenericMeasure
    {
        internal enum MeasureType
        {
            current,
            lastX,
            max,
            min
        }

        internal MeasureType Type = MeasureType.current;

        internal virtual void Dispose()
        {
        }

        internal virtual void Reload(Rainmeter.API api, ref double maxValue)
        {
            string type = api.ReadString("Type", "current");
            switch (type.ToLowerInvariant())
            {
                case "current":
                    Type = MeasureType.current;
                    break;

                case "lastx":
                    Type = MeasureType.lastX;
                    break;

                case "min":
                    Type = MeasureType.min;
                    break;

                case "max":
                    Type = MeasureType.max;
                    break;

                default:
                    api.Log(API.LogType.Error, "ZWIFT_RM_API.dll: Type=" + type + " not valid");
                    break;
            }
        }

        internal virtual double Update()
        {
            return 0.0;
        }
    }

    internal class ZwiftMeasure : GenericMeasure
    {
        internal string Name;
        internal IntPtr Skin;
        internal Rainmeter.API lapi = null;

        internal string user = "";
        internal string password = "";
        internal int pastMonth = 0;
        internal static JavaScriptSerializer serializer = new JavaScriptSerializer();
        Dictionary<DateTime, double> distanceDict = new Dictionary<DateTime, double>();

        HttpClientHandler handler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        private readonly HttpClient client = null;

        internal ZwiftMeasure(Rainmeter.API api)
        {
            lapi = api;
            client = new HttpClient(handler);
            serializer.RegisterConverters(new[] { new DynamicJsonConverter() });
        }

        internal override void Dispose()
        {
        }

        internal string fetchZwiftAuthToken()
        {
            if (user.Length < 1 || password.Length < 1) return null;

            var qs = new Dictionary<string, string>
            {
                { "client_id", "Zwift_Mobile_Link" },
                { "grant_type", "password" },
                { "username", user },
                { "password", password }
            };

            var content = new FormUrlEncodedContent(qs);
            try
            {
                var response = client.PostAsync("https://secure.zwift.com/auth/realms/zwift/tokens/access/codes", content).Result;
                if (response.IsSuccessStatusCode)
                {
                    var json = response.Content.ReadAsStringAsync().Result;
                    dynamic result = serializer.Deserialize(json, typeof(object));
                    return result.access_token;
                }
            }
            catch (Exception e)
            {
                lapi.Log(API.LogType.Error, "ZWIFT_RM_API.dll: fetchZwiftAuthToken error: " + e.Message);
            };
            return "";
        }

        internal dynamic fetchZwiftFeed()
        {
            var token = fetchZwiftAuthToken();
            if (token.Length < 1) return null;

            try
            {
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
                client.DefaultRequestHeaders.Add("User-Agent", "Zwift/115 CFNetwork/758.0.2 Darwin/15.0.0");
                client.DefaultRequestHeaders.Add("Connection", "keep-alive");

                HttpResponseMessage responseProfile = client.GetAsync("https://us-or-rly101.zwift.com/api/profiles/me/").Result;
                if (responseProfile.IsSuccessStatusCode)
                {
                    var jsonProfile = responseProfile.Content.ReadAsStringAsync().Result;
                    dynamic resultProfile = serializer.Deserialize(jsonProfile, typeof(object));

                    List<dynamic> result = new List<dynamic>();
                    string url = "https://us-or-rly101.zwift.com/api/profiles/" + resultProfile.id + "/activities/feed";

                    // we fetch all activities all the way back... todo: only fetch last X as requested -> small db to cache would be nice
                    while (true)
                    {
                        HttpResponseMessage responseFeed = client.GetAsync(url).Result;
                        if (responseFeed.IsSuccessStatusCode)
                        {
                            var jsonFeed = responseFeed.Content.ReadAsStringAsync().Result;
                            dynamic resultFeed = serializer.Deserialize(jsonFeed, typeof(List<object>));
                            result.AddRange(resultFeed);
                            if (resultFeed.Count < 1 || !responseFeed.Headers.Contains("Link")) break;
                            url = responseFeed.Headers.GetValues("Link").FirstOrDefault().Replace("<", "").Replace(">; rel=\"next\"", "");
                        }
                        else
                        {
                            break;
                        }
                    }
                    return result;
                }
            }
            catch (Exception e)
            {
                lapi.Log(API.LogType.Error, "ZWIFT_RM_API.dll: fetchZwiftFeed error: " + e.Message);
            };
            return null;
        }


        /*
         * Example activity element in array, notice each entry has profile info as well as "sport" on case RUN and cycling should be sepearte
         
        {
            id_str: '1234567890',
            id: 1234567890,
            profileId: 1234567890,
            profile: {
              id: 1234567890,
              publicId: '1234567890-1234567890-1234567890',
              firstName: 'Example',
              lastName: 'Name',
              male: true,
              imageSrc: 'https://static-cdn.zwift.com/prod/profile/12345678901234567890',
              imageSrcLarge: 'https://static-cdn.zwift.com/prod/profile/12345678901234567890',
              playerType: 'NORMAL',
              countryAlpha3: 'deu',
              countryCode: 276,
              useMetric: true,
              riding: false,
              privacy: [Object],
              socialFacts: null,
              worldId: null,
              enrolledZwiftAcademy: false,
              playerTypeId: 1,
              playerSubTypeId: null,
              currentActivityId: null
            },
            worldId: 1,
            name: 'Zwift Run - Watopia',
            description: null,
            privateActivity: true,
            sport: 'RUNNING',
            startDate: '2021-01-01T00:00:00.000+0000',
            endDate: '2021-01-01T01:00:00.00+0000',
            lastSaveDate: '2021-01-01T01:00:00.00+0000',
            autoClosed: false,
            duration: '1:00',
            distanceInMeters: 1234.56,
            fitFileBucket: 's3-fit-prd-uswest2-zwift',
            fitFileKey: 'prod/123456/123456-123456123456',
            totalElevation: 0,
            avgWatts: 0,
            rideOnGiven: false,
            activityRideOnCount: 42,
            activityCommentCount: 0,
            snapshotList: null,
            calories: 4242.00,
            primaryImageUrl: 'https://s3-fit-prd-uswest2-zwift.s3.amazonaws.com/prod/img/123456-123456123456123456',
            movingTimeInMs: 123456,
            privacy: 'PRIVATE',
            topNotableMoment: {
              notableMomentTypeId: 1,
              activityId: 123456123456123456123456,
              incidentTime: 123456,
              priority: 8,
              aux1: '3',
              aux2: '300'
            },
            avgSpeedInMetersPerSecond: 2.42,
            feedImageThumbnailUrl: 'https://s3-fit-prd-uswest2-zwift.s3.amazonaws.com/prod/img/1234561-23456123456123456',
            eventSubgroupId: null,
            eventId: null,
            clubActivity: false
          },
         
         */

        internal override void Reload(Rainmeter.API api, ref double maxValue)
        {
            base.Reload(api, ref maxValue);
            Name = api.GetMeasureName();
            Skin = api.GetSkin();

            user = api.ReadString("zwiftUser", "");
            password = api.ReadString("zwiftPass", "");
            pastMonth = api.ReadInt("pastMonth", 0);

            var feedArr = fetchZwiftFeed();

            try
            {
                foreach (var activity in feedArr)
                {
                    CultureInfo provider = CultureInfo.InvariantCulture;
                    DateTime start = DateTime.Parse(activity.startDate);
                    var month = new DateTime(start.Year, start.Month, 1);
                    if (!distanceDict.ContainsKey(month)) distanceDict[month] = 0;
                    distanceDict[month] += Convert.ToDouble(activity.distanceInMeters);
                }
            }
            catch (Exception e)
            {
                api.Log(API.LogType.Error, "ZWIFT_RM_API.dll: Reload error:" + e.Message);
            };

        }

        internal override double Update()
        {
            //retrive value based on type
            if (distanceDict.Count < 1 || !distanceDict.ContainsKey(new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)))
                return 0.0f;


            switch (Type)
            {
                case MeasureType.current:
                    {
                        return distanceDict[new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)];
                    }
                case MeasureType.lastX:
                    {
                        var date = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-pastMonth);
                        return distanceDict[date];
                    }
                case MeasureType.min:
                    {
                        return distanceDict.Values.Min();
                    }
                case MeasureType.max:
                    {
                        return distanceDict.Values.Max();
                    }
            }
            return 0.0;
        }
    }

    public static class Plugin
    {
        [DllExport]
        public static void Initialize(ref IntPtr data, IntPtr rm)
        {
            Rainmeter.API api = new Rainmeter.API(rm);
            GenericMeasure measure = new ZwiftMeasure(rm);
            data = GCHandle.ToIntPtr(GCHandle.Alloc(measure));
        }

        [DllExport]
        public static void Finalize(IntPtr data)
        {
            GenericMeasure measure = (GenericMeasure)GCHandle.FromIntPtr(data).Target;
            measure.Dispose();
            GCHandle.FromIntPtr(data).Free();
        }

        [DllExport]
        public static void Reload(IntPtr data, IntPtr rm, ref double maxValue)
        {
            GenericMeasure measure = (GenericMeasure)GCHandle.FromIntPtr(data).Target;
            measure.Reload(new Rainmeter.API(rm), ref maxValue);
        }

        [DllExport]
        public static double Update(IntPtr data)
        {
            GenericMeasure measure = (GenericMeasure)GCHandle.FromIntPtr(data).Target;
            return measure.Update();
        }
    }
}
