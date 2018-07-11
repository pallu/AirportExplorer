using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

using GoogleApi;
using GoogleApi.Entities.Common;
using GoogleApi.Entities.Common.Enums;
using GoogleApi.Entities.Places.Details.Request;
using GoogleApi.Entities.Places.Photos.Request;
using GoogleApi.Entities.Places.Search.NearBy.Request;
using MaxMind.GeoIP2;

namespace AirportExplorer.Pages
{
    public class IndexModel : PageModel
    {
        public string MapboxAccessToken { get; private set; }
        public string GoogleApiKey { get; private set; }
        public double InitialLatitude { get; set; } = 0;
        public double InitialLongitude { get; set; } = 0;
        public int InitialZoom { get; set; } = 1;


        private readonly IHostingEnvironment _hostingEnvironment;

        public IndexModel(IConfiguration configuration, IHostingEnvironment hostingEnvironment)
        {
            MapboxAccessToken = configuration["Mapbox:AccessToken"];
            GoogleApiKey = configuration["Google:ApiKey"];
            _hostingEnvironment = hostingEnvironment;
        }
        public void OnGet()
        {
            try
            {
                using (var dbReader = new DatabaseReader(Path.Combine(_hostingEnvironment.WebRootPath, "GeoLite2-City.mmdb")))
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress;

                    var city = dbReader.City(ipAddress);
                    if(city?.Location?.Latitude!=null && city?.Location.Longitude != null)
                    {
                        InitialLatitude = city.Location.Latitude.Value;
                        InitialLongitude = city.Location.Longitude.Value;
                        InitialZoom = 9;
                    }
                }
            }
            catch (Exception ex)
            {

                //throw;
            }
            
        }
        public IActionResult OnGetAirports()
        {
            var configuration = new Configuration
            {
                BadDataFound = context => { }
            };
            using (var sr = new StreamReader(Path.Combine(_hostingEnvironment.WebRootPath, "airports.dat")))
            {
                using (var reader = new CsvReader(sr,configuration))
                {
                    FeatureCollection featureCollection = new FeatureCollection();
                    while (reader.Read())
                    {
                        string name = reader.GetField<string>(1);
                        string iataCode = reader.GetField<string>(4);
                        double latitude = reader.GetField<double>(6);
                        double longitude = reader.GetField<double>(7);

                        featureCollection.Features.Add(new Feature(new Point(new Position(latitude, longitude)),
                            new Dictionary<string, object>
                            {
                                {"name", name},
                                {"iataCode", iataCode}
                            }));
                    }
                    return new JsonResult(featureCollection);
                }
            }
            
        }

        public async Task<IActionResult> OnGetAirportDetail(string name, double latitude, double longitude)
        {
            var airportDetail = new AirportDetail();

            var searchResponse = await GooglePlaces.NearBySearch.QueryAsync(new PlacesNearBySearchRequest()
            {
                Key = GoogleApiKey,
                Name = name,
                Location = new Location(latitude, longitude),
                Radius = 1000
            });
            if (!searchResponse.Status.HasValue || searchResponse.Status.Value != Status.Ok || !searchResponse.Results.Any())
                return new BadRequestResult();

            //get first result
            var nearByResult = searchResponse.Results.FirstOrDefault();
            string placeId = nearByResult.PlaceId;
            string photoReference = nearByResult.Photos?.FirstOrDefault()?.PhotoReference;
            string photoCredit = nearByResult.Photos?.FirstOrDefault()?.HtmlAttributions.FirstOrDefault();

            //execute detail response
            var detailsResponse = await GooglePlaces.Details.QueryAsync(new PlacesDetailsRequest()
            {
                Key = GoogleApiKey,
                PlaceId = placeId
            });

            if (!detailsResponse.Status.HasValue || detailsResponse.Status.Value != Status.Ok)
                return new BadRequestResult();
            //set details
            var detailsResult = detailsResponse.Result;
            airportDetail.FormattedAddress = detailsResult.FormattedAddress;
            airportDetail.PhoneNumber = detailsResult.FormattedPhoneNumber;
            airportDetail.Website = detailsResult.Website;

            if(photoReference != null)
            {
                //get the photo
                var photosResponse = await GooglePlaces.Photos.QueryAsync(new PlacesPhotosRequest()
                {
                    Key = GoogleApiKey,
                    PhotoReference = photoReference,
                    MaxWidth = 400
                });
                if(photosResponse.Buffer!=null)
                {
                    airportDetail.Photo = Convert.ToBase64String(photosResponse.Buffer);
                    airportDetail.PhotoCredit = photoCredit;
                }
            }
            return new JsonResult(airportDetail);
        }
    }
}
