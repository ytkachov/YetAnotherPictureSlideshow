using System.Drawing;

public class FinfoData
{
    public Rectangle[] Faces { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string PlaceName { get; set; }
    public string NominatimData { get; set; }
    public bool GeocodingAttempted { get; set; }
    public bool ExifReadFailed { get; set; }
}
