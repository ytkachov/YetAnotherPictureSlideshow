using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Policy;
using System.Text;
using System.Xml.Linq;
using OpenQA.Selenium;

using Newtonsoft.Json;

namespace weather
{

  [DataContract]
  public class YandexWeather
  {
    [DataMember(Name = "now")]
    public long Now { get; set; }

    [DataMember(Name = "now_dt")]
    public string NowDt { get; set; }
    
    [DataMember(Name = "info")]
    public YandexWeatherInfo Info { get; set; }
    
    [DataMember(Name = "fact")]
    public YandexWeatherFact Fact { get; set; }
    
    [DataMember(Name = "forecasts")]
    public List<YandexWeatherForecast> Forecasts { get; set; }
  }

  [DataContract(Name = "fact")]
  public class YandexWeatherFact
  {
    [DataMember(Name = "temp")]
    public double Temp { get; set; }
    
    [DataMember(Name = "feels_like")]
    public double FeelsLike { get; set; }
    
    [DataMember(Name = "icon")]
    public string Icon { get; set; }
  
    [DataMember(Name = "condition")]
    public string Condition { get; set; }
    
    [DataMember(Name = "wind_speed")]
    public double WindSpeed { get; set; }
    
    [DataMember(Name = "wind_gust")]
    public double WindGust { get; set; }
    
    [DataMember(Name = "wind_dir")]
    public string WindDir { get; set; }

    [DataMember(Name = "wind_angle")]
    public int WindAngle {get; set; }

    [DataMember(Name = "pressure_mm")]
    public double PressureMm { get; set; }
    
    [DataMember(Name = "pressure_pa")]
    public double PressurePa { get; set; }
    
    [DataMember(Name = "humidity")]
    public double Humidity { get; set; }
    
    [DataMember(Name = "daytime")]
    public string Daytime { get; set; }
    
    [DataMember(Name = "polar")]
    public bool Polar { get; set; }
    
    [DataMember(Name = "season")]
    public string Season { get; set; }
    
    [DataMember(Name = "obs_time")]
    public double ObsTime { get; set; }
  }

  [DataContract]
  public class YandexWeatherForecast
  {
    [DataMember(Name = "date")]
    public string Date { get; set; }

    [DataMember(Name = "date_ts")]
    public long DateTs { get; set; }

    [DataMember(Name = "week")]
    public long Week { get; set; }

    [DataMember(Name = "sunrise")]
    public string Sunrise { get; set; }
    
    [DataMember(Name = "sunset")]
    public string Sunset { get; set; }

    [DataMember(Name = "parts")]
    public YandexWeatherParts Parts { get; set; }

    [DataMember(Name = "hours")]
    public List<YandexWeatherHourForecast> Hours { get; set; }
  }

  [DataContract]
  public class YandexWeatherParts
  {
    [DataMember(Name = "day")]
    public YandexWeatherDayPartForecast Day { get; set; }

    [DataMember(Name = "day_short")]
    public YandexWeatherDayPartForecast DayShort { get; set; }

    [DataMember(Name = "evening")]
    public YandexWeatherDayPartForecast Evening { get; set; }

    [DataMember(Name = "morning")]
    public YandexWeatherDayPartForecast Morning { get; set; }

    [DataMember(Name = "night")]
    public YandexWeatherDayPartForecast Night { get; set; }

    [DataMember(Name = "night_short")]
    public YandexWeatherDayPartForecast NightShort{ get; set; }
  }

  [DataContract]
  public class YandexWeatherDayPartForecast
  {
    [DataMember(Name = "_source")]
    public string Source { get; set; }
    
    [DataMember(Name = "cloudness")]
    public double Cloudness { get; set; }
    
    [DataMember(Name = "uv_index")]
    public double UVIndex { get; set; }

    [DataMember(Name = "temp_min")]
    public long TempMin { get; set; }

    [DataMember(Name = "temp_max")]
    public long TempMax { get; set; }

    [DataMember(Name = "temp_avg")]
    public long TempAvg { get; set; }

    [DataMember(Name = "feels_like")]
    public long FeelsLike { get; set; }

    [DataMember(Name = "icon")]
    public string Icon { get; set; }

    [DataMember(Name = "condition")]
    public string Condition { get; set; }

    [DataMember(Name = "polar")]
    public bool Polar { get; set; }

    [DataMember(Name = "wind_speed")]
    public double WindSpeed { get; set; }
    
    [DataMember(Name = "wind_gust")]
    public double WindGust { get; set; }
    
    [DataMember(Name = "wind_dir")]
    public string WindDir { get; set; }
    
    [DataMember(Name = "wind_angle")]
    public long WindAngle { get; set; }
    
    [DataMember(Name = "humidity")]
    public long Humidity { get; set; }
  }

  [DataContract]
  public class YandexWeatherHourForecast
  {
    [DataMember(Name = "hour")]
    public string Hour { get; set; }

    [DataMember(Name = "hour_ts")]
    public long HourTS { get; set; }

    [DataMember(Name = "cloudness")]
    public double Cloudness { get; set; }

    [DataMember(Name = "is_thunder")]
    public bool IsThunder { get; set; }

    [DataMember(Name = "uv_index")]
    public double UVIndex { get; set; }

    [DataMember(Name = "temp")]
    public long Temp { get; set; }

    [DataMember(Name = "feels_like")]
    public long FeelsLike { get; set; }

    [DataMember(Name = "icon")]
    public string Icon { get; set; }

    [DataMember(Name = "condition")]
    public string Condition { get; set; }

    [DataMember(Name = "polar")]
    public bool Polar { get; set; }

    [DataMember(Name = "wind_speed")]
    public double WindSpeed { get; set; }

    [DataMember(Name = "wind_gust")]
    public double WindGust { get; set; }

    [DataMember(Name = "wind_dir")]
    public string WindDir { get; set; }

    [DataMember(Name = "wind_angle")]
    public long WindAngle { get; set; }

    [DataMember(Name = "humidity")]
    public long Humidity { get; set; }
  }

  [DataContract]
  public class YandexWeatherInfo
  {
    [DataMember(Name = "lat")]
    public double Lat { get; set; }

    [DataMember(Name = "lon")]
    public double Lon { get; set; }

    [DataMember(Name = "url")]
    public string Url { get; set; }

    [DataMember(Name = "def_pressure_mm")]
    public long DefPressureMm { get; set; }

    [DataMember(Name = "def_pressure_pa")]
    public long DefPressurePa { get; set; }
  }

  public class YandexApiReader : IWeatherReader
  {
    protected string _url;
    protected string _header_key;
    protected YandexWeather _yandex_weather;
    protected DateTime _last_request_time = DateTime.MinValue;

    public YandexApiReader(string key, double lat, double lon)
    {
      _url = $"https://api.weather.yandex.ru/v2/forecast?lat={lat}&lon={lon}";
      _header_key = $"X-Yandex-Weather-Key: {key}";
    }

    public void close( )
    {
    }

    public string current( )
    {
      if (_yandex_weather == null ) 
        make_request();

      if (_yandex_weather == null)
        return "";

      _yandex_weather.Fact.PressureMm = _yandex_weather.Info.DefPressureMm;
      _yandex_weather.Fact.PressurePa = _yandex_weather.Info.DefPressurePa;
      return JsonConvert.SerializeObject(_yandex_weather.Fact, Formatting.Indented, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
    }

    public string forecast( )
    {
      if (_yandex_weather == null)
        make_request();

      if (_yandex_weather == null)
        return "";

      return JsonConvert.SerializeObject(_yandex_weather.Forecasts, Formatting.Indented, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
    }

    public void getrest( )
    {
    }

    public void restart( )
    {
    }

    public string temperature( )
    {
      return "88.8";
    }

    private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

    protected void make_request()
    {
      _last_request_time = DateTime.Now;

      using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, _url);
      var sep = _header_key.IndexOf(':');
      if (sep > 0)
        request.Headers.TryAddWithoutValidation(_header_key.Substring(0, sep).Trim(), _header_key.Substring(sep + 1).Trim());

      using var response = _httpClient.Send(request);
      response.EnsureSuccessStatusCode();
      using var stream = response.Content.ReadAsStream();
      using var reader = new StreamReader(stream);
      string line = reader.ReadToEnd();
      _yandex_weather = JsonConvert.DeserializeObject<YandexWeather>(line, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
    }
  }

  public class YandexApiReaderWriter : WeatherFileReaderWriter
  {
    private WeatherSeleniumReader _nsu_temperature_reader = null;
    private YandexApiReader _yandex_api_reader = null;

    public YandexApiReaderWriter(string key = "", double lat = 54.85194397, double lon = 83.10189056) : base()
    {
      if (!string.IsNullOrEmpty(key))
      {
        _nsu_temperature_reader = new NsuTemperatureReader();
        _yandex_api_reader = new YandexApiReader(key, lat, lon);
      }

      _foldername = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
      _filename = "yandex_weather_api_info.txt";
      _execparams = $". {(int)WeatherSource.YAC}";
    }

    public override void close( )
    {
      if (_nsu_temperature_reader != null)
        _nsu_temperature_reader.close();
        
      base.close();
    }

    public override string temperature( )
    {
      if (_nsu_temperature_reader == null)
        return base.temperature( );

      return _nsu_temperature_reader.temperature();
    }

    public override string current( )
    {
      if (_yandex_api_reader == null || FileIsFresh())
        return base.current();

      return _yandex_api_reader.current();
    }

    public override string forecast( )
    {
      if (_yandex_api_reader == null || FileIsFresh())
        return base.forecast();

      return _yandex_api_reader.forecast();
    }

    private bool FileIsFresh()
    {
      string fname = Path.Combine(_foldername, _filename);
      if (!File.Exists(fname))
        return false;

      double span_minutes = 120;
      if (DateTime.Now.Hour > 22 || DateTime.Now.Hour < 7)
        span_minutes = 240;

      var ft = File.GetLastWriteTime(fname);
      if (DateTime.Now - ft > TimeSpan.FromMinutes(span_minutes))
        return false;

      try
      {
        long obs_time = (long)(JsonConvert.DeserializeObject<YandexWeatherFact>(base.current(), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto }).ObsTime);
        long cur_time = DateTimeOffset.Now.ToUnixTimeSeconds();

        if (cur_time - obs_time > span_minutes * 60)
          return false;
      }
      catch 
      {
        return false;
      }

      return true;
    }
  }

  public class NsuTemperatureReader : WeatherSeleniumReader
  {
    public NsuTemperatureReader( ) : base (WeatherSource.YC)
    {
    }

    protected override string get_current( ) { return ""; }
    protected override string get_forecast( ) { return ""; }
  }
}
