using System.Text.Json.Serialization;

namespace SynologySlideshow.Core.Models;

public class Address
{
    [JsonPropertyName("country")]
    public string? Country { get; set; }
    [JsonPropertyName("country_id")]
    public string? CountryId { get; set; }
    [JsonPropertyName("state")]
    public string? State { get; set; }
    [JsonPropertyName("state_id")]
    public string? StateId { get; set; }
    [JsonPropertyName("county")]
    public string? County { get; set; }
    [JsonPropertyName("county_id")]
    public string? CountyId { get; set; }
    [JsonPropertyName("city")]
    public string? City { get; set; }
    [JsonPropertyName("city_id")]
    public string? CityId { get; set; }
    [JsonPropertyName("town")]
    public string? Town { get; set; }
    [JsonPropertyName("town_id")]
    public string? TownId { get; set; }
    [JsonPropertyName("district")]
    public string? District { get; set; }
    [JsonPropertyName("district_id")]
    public string? DistrictId { get; set; }
    [JsonPropertyName("village")]
    public string? Village { get; set; }
    [JsonPropertyName("village_id")]
    public string? VillageId { get; set; }
    [JsonPropertyName("route")]
    public string? Route { get; set; }
    [JsonPropertyName("route_id")]
    public string? RouteId { get; set; }
    [JsonPropertyName("landmark")]
    public string? Landmark { get; set; }
    [JsonPropertyName("landmark_id")]
    public string? LandmarkId { get; set; }
}