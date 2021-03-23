using SynologySlideshow.Core.Models;
using SynologyNet.Models.Responses;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SynologySlideshow.Core;

public class SynologyAlbumSource
{
    private readonly HttpClient _client;
    private string? _sid;

    public SynologyAlbumSource(string host)
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri(host)
        };
    }

    public async Task Login(string user, string password)
    {
        var data = new Dictionary<string, string>()
        {
            { "api", "SYNO.API.Auth"},
            { "method", "login"},
            { "version", "3" },
            { "query", "" },
            { "account", user },
            { "passwd", password }
        };
        var r = await _client.PostAsync("auth.cgi", new FormUrlEncodedContent(data));
        r.EnsureSuccessStatusCode();
        _sid = ((await r.Content.ReadFromJsonAsync<BaseDataResponse<AuthenticationResponse>>())!).Data!.Sid;
    }

    public async Task<PhotoAlbum[]> GetAlbums()
    {
        var data = await Invoke<ListObject<JsonNode>>("SYNO.Foto.Browse.Album", 3, "list", new Dictionary<string, string>()
        {
            { "category", "normal_share_with_me" },
            { "offset", "0" },
            { "limit", "100" },
            { "sort_by", "create_time" },
            { "sort_direction", "asc" },
            { "additional", JsonSerializer.Serialize(new[]{"thumbnail"})}
        });
        return data.List.Select(x => x.Deserialize<PhotoAlbum>()).Select(x => x!.WithThumbnail(GetThumbnailUri(x.Additional.Thumbnail, x.Passphrase))).ToArray();
    }

    public async Task<PhotoSlide[]> GetSlides(PhotoAlbum album)
    {
        var additional = JsonSerializer.Serialize(new[]
        {
            "orientation",
            "resolution",
            "thumbnail",
            //"video_convert",
            //"video_meta",
            //"provider_user_id",
            //"exif",
            "tag",
            "description",
            //"gps",
            //"geocoding_id",
            "address",
            "person",
        });
        var photos = (await Invoke<ListObject<JsonNode>>("SYNO.Foto.Browse.Item", 3, "list", new Dictionary<string, string>()
        {
            {"album_id", string.IsNullOrEmpty( album.Passphrase)? album.Id.ToString():string.Empty},
            {"passphrase", album.Passphrase},
            {"offset", "0"},
            {"limit", album.ItemCount.ToString()},
            //{ "sort_by", album.SortBy },
            //{ "sort_direction", album.SortDirection },
            {"additional", additional },
        })).List.ToArray();

        return (from np in (from n in photos
                            select (n, n.Deserialize<PhotoSlide>())).DistinctBy(np => new { np.Item2.Filename, np.Item2.Time }) // avoid duplicates by filename/timestamp
                let n = np.n
                let p = np.Item2
                let location = p.Additional.Address switch
                {
                    null => null,
                    var a => string.Join(", ", (a switch
                    {
                        {
                            Landmark: "",
                            Village: "",
                            Town: "",
                            City: "",
                            Country: "Denmark",
                            CountyId: { } county
                        } when county != "" => new[]
                        {
                            county
                        },
                        {
                            Landmark: "",
                            Village: "",
                            Town: "",
                            City: "",
                            Country: "Denmark",
                            StateId: { } state
                        } when state != "" => new[]
                        {
                            state
                        },
                        {
                            Landmark: "",
                            Village: "",
                            Town: "",
                            City: "",
                            Country: "Denmark",
                            CountryId: { } country
                        } when country != "" => new[]
                        {
                            country
                        },
                        { Country: "Denmark" } => new[]
                        {
                            a.LandmarkId,
                            a.VillageId,
                            a.TownId,
                            a.CityId
                        },
                        _ => new[]
                        {
                            a.LandmarkId,
                            a.VillageId,
                            a.TownId,
                            a.CityId,
                            a.CountyId,
                            a.StateId,
                            a.CountryId
                        }
                    }).Where(x => !string.IsNullOrEmpty(x)).Distinct())
                }
                where p.Type is "photo" or "live"
                let thumb = GetThumbnailUri(p.Additional.Thumbnail, album.Passphrase)
                select p.Extend(thumb, location, n)).ToArray();
    }

    public string GetThumbnailUri(PhotoThumbnail thumb, string passphrase)
    {
        return BuildUri("SYNO.FotoTeam.Thumbnail", 2, "get", new Dictionary<string, string>
        {
            { "id", thumb.UnitId.ToString() },
            { "cache_key", thumb.CacheKey },
            { "type", "unit" },
            { "size", "xl" },
            { "passphrase", passphrase },
        });
    }
    private async Task<T> Invoke<T>(string api, int version, string method,
        IEnumerable<KeyValuePair<string, string>>? parameters = default)
    {
        var r = await _client.GetAsync(BuildUri(api, version, method, parameters));
        r.EnsureSuccessStatusCode();
        var response = (await r.Content.ReadFromJsonAsync<BaseDataResponse<T>>());
        if (response == null || !response.Success || response.Data == null)
            throw new Exception();
        return response.Data;
    }

    string BuildUri(string api, int version, string method,
        IEnumerable<KeyValuePair<string, string>>? parameters = default)
    {
        var data = new Dictionary<string, string>()
        {
            { "api", api },
            { "method", method },
            { "version", version.ToString() },
            { "query", "" },
        };
        if (parameters != null)
            foreach (var pair in parameters)
            {
                data.Add(pair.Key, pair.Value);
            }

        data.Add("_sid", _sid!);

        return new Uri(_client.BaseAddress!, "entry.cgi") + ToQueryString(data);
    }

    private string ToQueryString(IDictionary<string, string> nvc)
    {
        var array = (
            from kv in nvc
            select string.Format(
                "{0}={1}",
                WebUtility.UrlEncode(kv.Key),
                WebUtility.UrlEncode(kv.Value))
        ).ToArray();
        return "?" + string.Join("&", array);
    }

}

static class StringHelpers
{
    public static string? IfNullOrEmptyString(this string? s, string? value) => string.IsNullOrEmpty(s) ? value : s;
    public static string? NullIfEmpty(this string? s) => string.IsNullOrEmpty(s) ? null : s;
}