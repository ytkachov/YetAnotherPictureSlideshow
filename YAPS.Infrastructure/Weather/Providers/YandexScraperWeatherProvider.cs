using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.Extensions.Options;
using OpenQA.Selenium;
using Yaps.Core.Models.Weather;
using Yaps.Infrastructure.Weather.Parsing;
using Yaps.Infrastructure.Weather.Selenium;

namespace Yaps.Infrastructure.Weather.Providers;

/// <summary>
/// Scrapes yandex.ru/pogoda (HTML, not the JSON API). The XPath
/// selectors are inherited from the legacy <c>YandexWeatherExtractor</c>;
/// the parsing pipeline is the same (collect outerHTML, normalise to
/// XML, run XPath) but the driver is now per-request.
/// </summary>
public sealed class YandexScraperWeatherProvider : SeleniumWeatherProviderBase
{
    private static readonly WeatherPeriod[] _periodOrder =
    {
        WeatherPeriod.TodayMorning,            WeatherPeriod.TodayDay,            WeatherPeriod.TodayEvening,            WeatherPeriod.TodayNight,
        WeatherPeriod.TomorrowMorning,         WeatherPeriod.TomorrowDay,         WeatherPeriod.TomorrowEvening,         WeatherPeriod.TomorrowNight,
        WeatherPeriod.DayAfterTomorrowMorning, WeatherPeriod.DayAfterTomorrowDay, WeatherPeriod.DayAfterTomorrowEvening, WeatherPeriod.DayAfterTomorrowNight
    };

    private readonly IOptions<WeatherOptions> _options;

    public YandexScraperWeatherProvider(SeleniumDriverFactory factory, IOptions<WeatherOptions> options)
        : base(factory)
    {
        _options = options;
    }

    public override string Name => "yandex-scrape";
    public override WeatherCapabilities Capabilities => WeatherCapabilities.All;

    protected override WeatherSnapshot? ExtractCurrent(IWebDriver driver, CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        var url = $"https://yandex.ru/pogoda/?lat={opts.Latitude.ToString(CultureInfo.InvariantCulture)}&lon={opts.Longitude.ToString(CultureInfo.InvariantCulture)}";
        driver.Navigate().GoToUrl(url);
        cancellationToken.ThrowIfCancellationRequested();

        var bundle = new StringBuilder("<current>\n");
        var info = TryFind(driver, By.XPath("//div[@class='fact__temp-wrap']"));
        if (info is not null) bundle.Append(OuterHtml(driver, info));

        var props = TryFind(driver, By.XPath("//div[@class='fact__props']"));
        if (props is not null) bundle.Append('\n').Append(OuterHtml(driver, props));

        bundle.Append("\n</current>");
        var xml = HtmlToXml.Parse(bundle.ToString());

        var temp = ParseTemperature(xml.DocumentElement?.SelectSingleNode("./div/a//div/span[@class='temp__value temp__value_with-unit']"));
        var windSpeed = TryParseDouble(xml.DocumentElement?.SelectSingleNode("./div[@class='fact__props']//span[@class='wind-speed']")?.InnerText);
        var windDirText = xml.DocumentElement?.SelectSingleNode("./div[@class='fact__props']//span[@class='fact__unit']/abbr")?.InnerText;
        var humidity = TryParseDouble(xml.DocumentElement?.SelectSingleNode("./div[@class='fact__props']//div[@class='term term_orient_v fact__humidity']/div[@class='term__value']")?.InnerText?.Replace('%', ' '));
        var pressureRaw = xml.DocumentElement?.SelectSingleNode("./div[@class='fact__props']//div[@class='term term_orient_v fact__pressure']")?.InnerText;
        var pressure = TryParseDouble(pressureRaw?.Split(' ')[0]);
        var iconClass = TryFind(driver, By.XPath("//div[@class='fact__temp-wrap']//img"))?.GetAttribute("class");

        return new WeatherSnapshot
        {
            TemperatureCelsius = temp,
            WeatherType = WeatherTypeMap.Lookup(WeatherTypeMap.YandexHtml, ExtractIconToken(iconClass)),
            WindDirection = WindDirectionMap.Lookup(WindDirectionMap.YandexHtml, windDirText),
            WindSpeedMs = windSpeed,
            Pressure = pressure,
            Humidity = humidity,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Source = Name
        };
    }

    protected override WeatherForecast? ExtractForecast(IWebDriver driver, CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        var url = $"https://yandex.ru/pogoda/details?lat={opts.Latitude.ToString(CultureInfo.InvariantCulture)}&lon={opts.Longitude.ToString(CultureInfo.InvariantCulture)}&via=ms";
        driver.Navigate().GoToUrl(url);
        cancellationToken.ThrowIfCancellationRequested();

        var cards = TryFindAll(driver, By.XPath("//div[@class='card']"));
        if (cards.Count == 0)
            cards = TryFindAll(driver, By.XPath("//article[@class='card']"));
        if (cards.Count == 0)
            return null;

        var bundle = new StringBuilder("<forecast>\n");
        foreach (var card in cards)
            bundle.Append(OuterHtml(driver, card)).Append('\n');
        bundle.Append("</forecast>");

        var xml = HtmlToXml.Parse(bundle.ToString());
        var periods = new Dictionary<WeatherPeriod, WeatherPeriodForecast>();
        int ordinal = 0;

        foreach (XmlNode dayDiv in xml.DocumentElement!.ChildNodes)
        {
            if (dayDiv.NodeType != XmlNodeType.Element)
                continue;

            var rows = dayDiv.SelectNodes("./dd[@class='forecast-details__day-info']/table[@class='weather-table']/tbody[@class='weather-table__body']/tr[@class='weather-table__row']");
            if (rows is null || rows.Count == 0)
                rows = dayDiv.SelectNodes("./div[@class='forecast-details__day-info']/table[@class='weather-table']/tbody[@class='weather-table__body']/tr[@class='weather-table__row']");
            if (rows is null) continue;

            for (int p = 0; p < 4 && ordinal < _periodOrder.Length; p++, ordinal++)
            {
                if (p >= rows.Count) continue;
                var row = rows[p];
                if (row is null) continue;

                var period = _periodOrder[ordinal];
                periods[period] = ExtractDayPart(row, period);
            }

            if (ordinal >= _periodOrder.Length) break;
        }

        return new WeatherForecast
        {
            Periods = periods,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Source = Name
        };
    }

    private static WeatherPeriodForecast ExtractDayPart(XmlNode row, WeatherPeriod period)
    {
        var iconClass = row.SelectSingleNode("./td/img")?.SelectSingleNode("@class")?.Value;
        var temps = row.SelectNodes("./td//div/span[@class='temp__value temp__value_with-unit']");
        double? low = null, high = null;
        if (temps is not null && temps.Count >= 2)
        {
            low = ParseTemperature(temps[0]);
            high = ParseTemperature(temps[1]);
        }
        else if (temps is not null && temps.Count == 1)
        {
            low = high = ParseTemperature(temps[0]);
        }

        var pressure = TryParseDouble(row.SelectSingleNode("./td[@class='weather-table__body-cell weather-table__body-cell_type_air-pressure']")?.InnerText);
        var humidityRaw = row.SelectSingleNode("./td[@class='weather-table__body-cell weather-table__body-cell_type_humidity']")?.InnerText?.Replace('%', ' ');
        var humidity = TryParseDouble(humidityRaw);
        var windSpeed = TryParseDouble(row.SelectSingleNode("./td//div//span[@class='wind-speed']")?.InnerText);
        var windDirText = row.SelectSingleNode("./td//div[@class='weather-table__wind-direction']/abbr")?.InnerText;

        return new WeatherPeriodForecast
        {
            Period = period,
            Low = low,
            High = high,
            Pressure = pressure,
            Humidity = humidity,
            WindSpeedMs = windSpeed,
            WindDirection = WindDirectionMap.Lookup(WindDirectionMap.YandexHtml, windDirText),
            WeatherType = WeatherTypeMap.Lookup(WeatherTypeMap.YandexHtml, ExtractIconToken(iconClass, "icon_thumb_"))
        };
    }

    private static double? ParseTemperature(XmlNode? node)
    {
        if (node is null) return null;
        var s = node.InnerText.Replace(',', '.').Replace('−', '-').Replace('°', ' ').Trim();
        return TryParseDouble(s);
    }

    private static double? TryParseDouble(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Replace(',', '.').Replace('−', '-').Trim();
        return double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : (double?)null;
    }

    /// <summary>
    /// Pulls the icon-state token out of a Yandex CSS class like
    /// "icon icon_thumb_bkn-d icon_color_white" → "bkn-d". The legacy
    /// extractor used the same offset-based scan; preserved verbatim so
    /// any future site-class change is a one-line fix.
    /// </summary>
    private static string? ExtractIconToken(string? cssClass, string marker = "icon_thumb_")
    {
        if (string.IsNullOrEmpty(cssClass)) return null;
        var start = cssClass.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = cssClass.IndexOf(' ', start);
        return end > start ? cssClass.Substring(start, end - start) : cssClass.Substring(start);
    }
}
