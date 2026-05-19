using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using OpenQA.Selenium;
using Yaps.Core.Models.Weather;
using Yaps.Infrastructure.Weather.Parsing;
using Yaps.Infrastructure.Weather.Selenium;

namespace Yaps.Infrastructure.Weather.Providers;

/// <summary>
/// Scrapes pogoda.ngs.ru/academgorodok. Forecast HTML is read as a
/// 3-day table whose rows correspond to today / tomorrow / day-after.
/// Logic preserved 1:1 from the legacy <c>WeatherProviderNGS</c>; the
/// difference is lifecycle (per-request driver, async, returns POCOs).
/// </summary>
public sealed class NgsScraperWeatherProvider : SeleniumWeatherProviderBase
{
    private const string WeatherUrl = "https://pogoda.ngs.ru/academgorodok/";

    private static readonly IReadOnlyDictionary<string, WeatherPeriod>[] _dayPeriods =
    {
        new Dictionary<string, WeatherPeriod>
        {
            ["ночь"]  = WeatherPeriod.TodayNight,
            ["утро"]  = WeatherPeriod.TodayMorning,
            ["день"]  = WeatherPeriod.TodayDay,
            ["вечер"] = WeatherPeriod.TodayEvening
        },
        new Dictionary<string, WeatherPeriod>
        {
            ["ночь"]  = WeatherPeriod.TomorrowNight,
            ["утро"]  = WeatherPeriod.TomorrowMorning,
            ["день"]  = WeatherPeriod.TomorrowDay,
            ["вечер"] = WeatherPeriod.TomorrowEvening
        },
        new Dictionary<string, WeatherPeriod>
        {
            ["ночь"]  = WeatherPeriod.DayAfterTomorrowNight,
            ["утро"]  = WeatherPeriod.DayAfterTomorrowMorning,
            ["день"]  = WeatherPeriod.DayAfterTomorrowDay,
            ["вечер"] = WeatherPeriod.DayAfterTomorrowEvening
        }
    };

    public NgsScraperWeatherProvider(SeleniumDriverFactory factory) : base(factory)
    {
    }

    public override string Name => "ngs-scrape";
    public override WeatherCapabilities Capabilities => WeatherCapabilities.All;

    protected override WeatherSnapshot? ExtractCurrent(IWebDriver driver, CancellationToken cancellationToken)
    {
        driver.Navigate().GoToUrl(WeatherUrl);
        cancellationToken.ThrowIfCancellationRequested();

        var info = TryFind(driver, By.ClassName("today-panel__info"));
        if (info is null) return null;

        var raw = OuterHtml(driver, info);
        raw = Regex.Replace(raw, @"<img\s[^>]*?src\s*=\s*['""]([^ '""]*?)['""][^>]*?>", " ");
        var xml = HtmlToXml.Parse(raw);

        var iconNode = xml.DocumentElement?.SelectSingleNode("./div/div[starts-with(@class,'icon-weather-big ')]");
        var iconToken = iconNode?.SelectSingleNode("@class")?.Value?.Substring("icon-weather-big ".Length);

        var today = xml.DocumentElement?.SelectSingleNode("./div[@class='today-panel__info__main']");
        var curr = today?.SelectSingleNode("./div[starts-with(@class,'today-panel__info__main__item')]");
        if (curr is null) return null;

        var temp = TryParseDouble(curr.SelectSingleNode("./div/span/span[@class='value__main']")?.InnerText);
        var windIcon = curr.SelectSingleNode("./dl/dd/i[starts-with(@class,'icon-small icon-wind-')]")?.SelectSingleNode("@class")?.Value;
        var windDirToken = windIcon?.Substring("icon-small icon-wind-".Length);
        var pressureTitle = curr.SelectSingleNode("./dl/dd/i[@class='icon-small icon-pressure']")?.SelectSingleNode("@title")?.Value;
        var humidityTitle = curr.SelectSingleNode("./dl/dd/i[@class='icon-small icon-humidity']")?.SelectSingleNode("@title")?.Value;

        return new WeatherSnapshot
        {
            TemperatureCelsius = temp,
            WeatherType = WeatherTypeMap.Lookup(WeatherTypeMap.Ngs, iconToken),
            WindDirection = WindDirectionMap.Lookup(WindDirectionMap.Ngs, windDirToken),
            Pressure = TryParseDouble(SplitFirstToken(pressureTitle)),
            Humidity = TryParseDouble(SplitBeforePercent(humidityTitle)),
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Source = Name
        };
    }

    protected override WeatherForecast? ExtractForecast(IWebDriver driver, CancellationToken cancellationToken)
    {
        // current() above might already have loaded this URL; reuse the driver session.
        if (driver.Url != WeatherUrl)
        {
            driver.Navigate().GoToUrl(WeatherUrl);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var table = TryFind(driver, By.XPath("//table[@class='pgd-detailed-cards elements']"))
                    ?? TryFind(driver, By.XPath("//table[@class='pgd-detailed-cards elements pgd-hidden']"));
        if (table is null) return null;

        var xml = HtmlToXml.Parse(OuterHtml(driver, table));
        var rows = xml.DocumentElement?.SelectNodes("./tbody/tr");
        if (rows is null || rows.Count == 0) return null;

        var periods = new Dictionary<WeatherPeriod, WeatherPeriodForecast>();
        for (int day = 0; day < rows.Count && day < _dayPeriods.Length; day++)
        {
            var tr = rows[day];
            if (tr is null) continue;
            ExtractDay(day, tr, periods);
        }

        return new WeatherForecast
        {
            Periods = periods,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Source = Name
        };
    }

    private static void ExtractDay(int day, XmlNode tr, Dictionary<WeatherPeriod, WeatherPeriodForecast> bag)
    {
        var periodDivs = SelectCellDivs(tr, "elements__section-daytime");
        var temperatureDivs = SelectCellDivs(tr, "elements__section-temperature");
        var weatherDivs = SelectCellDivs(tr, "elements__section-weather");
        var windDivs = SelectCellDivs(tr, "elements__section-wind");
        var pressureDivs = SelectCellDivs(tr, "elements__section-pressure");
        var humidityDivs = SelectCellDivs(tr, "elements__section-humidity");

        int count = periodDivs.Count;
        if (count == 0 ||
            temperatureDivs.Count != count ||
            weatherDivs.Count != count ||
            windDivs.Count != count ||
            pressureDivs.Count != count ||
            humidityDivs.Count != count)
        {
            return;
        }

        var dayMap = _dayPeriods[day];
        for (int p = 0; p < count; p++)
        {
            var pname = periodDivs[p]!.InnerText.Trim();
            if (!dayMap.TryGetValue(pname, out var period)) continue;

            var temp = TryParseDouble(temperatureDivs[p]!.InnerText.Trim().Replace('−', '-'));
            var weatherIconClass = weatherDivs[p]!.SelectSingleNode("./i")?.SelectSingleNode("@class")?.Value;
            var weatherToken = StripClassPrefix(weatherIconClass, "icon-weather icon-weather-");

            var windIconClass = windDivs[p]!.SelectSingleNode("./i")?.SelectSingleNode("@class")?.Value;
            var windToken = StripClassPrefix(windIconClass, "icon-small icon-wind-");
            var windSpeed = TryParseDouble(SplitFirstToken(windDivs[p]!.InnerText.TrimStart()));

            var pressure = TryParseDouble(SplitFirstToken(pressureDivs[p]!.InnerText.TrimStart()));
            var humidity = TryParseDouble(SplitBeforePercent(humidityDivs[p]!.InnerText));

            bag[period] = new WeatherPeriodForecast
            {
                Period = period,
                Low = temp,
                High = temp,
                Pressure = pressure,
                Humidity = humidity,
                WindSpeedMs = windSpeed,
                WindDirection = WindDirectionMap.Lookup(WindDirectionMap.Ngs, windToken),
                WeatherType = WeatherTypeMap.Lookup(WeatherTypeMap.Ngs, weatherToken)
            };
        }
    }

    private static XmlNodeList SelectCellDivs(XmlNode tr, string cellClass)
    {
        var cell = tr.SelectSingleNode($"./td[@class='{cellClass}']");
        return cell?.SelectNodes("./div") ?? tr.OwnerDocument!.CreateDocumentFragment().ChildNodes;
    }

    private static string? StripClassPrefix(string? cssClass, string prefix)
    {
        if (string.IsNullOrEmpty(cssClass)) return null;
        if (!cssClass.StartsWith(prefix, StringComparison.Ordinal)) return cssClass;
        var rest = cssClass.Substring(prefix.Length);
        var sp = rest.IndexOf(' ');
        return sp < 0 ? rest : rest.Substring(0, sp);
    }

    private static string? SplitFirstToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        var sp = trimmed.IndexOf(' ');
        return sp < 0 ? trimmed : trimmed.Substring(0, sp);
    }

    private static string? SplitBeforePercent(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var pct = raw.IndexOf('%');
        return pct < 0 ? raw.Trim() : raw.Substring(0, pct).Trim();
    }

    private static double? TryParseDouble(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Replace(',', '.').Replace('−', '-').Trim();
        return double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : (double?)null;
    }
}
