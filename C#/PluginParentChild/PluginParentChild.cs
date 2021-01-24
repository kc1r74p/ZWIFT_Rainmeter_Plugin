using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Rainmeter;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Linq;
using System.Net;

namespace PluginParentChild
{
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
                    dynamic result = JsonConvert.DeserializeObject(json);
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
                    dynamic resultProfile = JsonConvert.DeserializeObject(jsonProfile);

                    List<dynamic> result = new List<dynamic>();
                    string url = "https://us-or-rly101.zwift.com/api/profiles/" + resultProfile.id + "/activities/feed";

                    // we fetch all activities all the way back... todo: only fetch last X as requested -> small db to cache would be nice
                    while (true)
                    {
                        HttpResponseMessage responseFeed = client.GetAsync(url).Result;
                        if (responseFeed.IsSuccessStatusCode)
                        {
                            var jsonFeed = responseFeed.Content.ReadAsStringAsync().Result;
                            dynamic resultFeed = JsonConvert.DeserializeObject<List<dynamic>>(jsonFeed, new JsonSerializerSettings()
                            {
                                DateTimeZoneHandling = DateTimeZoneHandling.Utc
                            });
                            result.AddRange(resultFeed);
                            if (resultFeed.Count < 1 || !responseFeed.Headers.Contains("Link")) break;
                            url = responseFeed.Headers.GetValues("Link").FirstOrDefault().Replace("<","").Replace(">; rel=\"next\"", "");
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
                    string startStr = activity.startDate;
                    DateTime start = DateTime.ParseExact(startStr, "MM/dd/yyyy HH:mm:ss", provider);
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
