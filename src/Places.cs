// Offline "near <city>" label for the configured coordinates - no geocoding service involved.
using System;

static class Places
{
    // name (de), name (en), lat, lon - larger cities in DACH plus nearby capitals
    static readonly object[][] Cities =
    {
        new object[] { "Berlin", "Berlin", 52.52, 13.40 },
        new object[] { "Hamburg", "Hamburg", 53.55, 9.99 },
        new object[] { "München", "Munich", 48.14, 11.58 },
        new object[] { "Köln", "Cologne", 50.94, 6.96 },
        new object[] { "Frankfurt am Main", "Frankfurt", 50.11, 8.68 },
        new object[] { "Stuttgart", "Stuttgart", 48.78, 9.18 },
        new object[] { "Düsseldorf", "Düsseldorf", 51.23, 6.78 },
        new object[] { "Leipzig", "Leipzig", 51.34, 12.37 },
        new object[] { "Dortmund", "Dortmund", 51.51, 7.47 },
        new object[] { "Essen", "Essen", 51.46, 7.01 },
        new object[] { "Bremen", "Bremen", 53.08, 8.80 },
        new object[] { "Dresden", "Dresden", 51.05, 13.74 },
        new object[] { "Hannover", "Hanover", 52.37, 9.73 },
        new object[] { "Nürnberg", "Nuremberg", 49.45, 11.08 },
        new object[] { "Duisburg", "Duisburg", 51.43, 6.76 },
        new object[] { "Bochum", "Bochum", 51.48, 7.22 },
        new object[] { "Wuppertal", "Wuppertal", 51.26, 7.15 },
        new object[] { "Bielefeld", "Bielefeld", 52.02, 8.53 },
        new object[] { "Bonn", "Bonn", 50.73, 7.10 },
        new object[] { "Münster", "Münster", 51.96, 7.63 },
        new object[] { "Mannheim", "Mannheim", 49.49, 8.47 },
        new object[] { "Karlsruhe", "Karlsruhe", 49.01, 8.40 },
        new object[] { "Augsburg", "Augsburg", 48.37, 10.90 },
        new object[] { "Wiesbaden", "Wiesbaden", 50.08, 8.24 },
        new object[] { "Mainz", "Mainz", 50.00, 8.27 },
        new object[] { "Aachen", "Aachen", 50.78, 6.08 },
        new object[] { "Kiel", "Kiel", 54.32, 10.14 },
        new object[] { "Rostock", "Rostock", 54.09, 12.10 },
        new object[] { "Magdeburg", "Magdeburg", 52.13, 11.63 },
        new object[] { "Freiburg", "Freiburg", 47.99, 7.85 },
        new object[] { "Erfurt", "Erfurt", 50.98, 11.03 },
        new object[] { "Kassel", "Kassel", 51.31, 9.48 },
        new object[] { "Saarbrücken", "Saarbrücken", 49.24, 7.00 },
        new object[] { "Regensburg", "Regensburg", 49.01, 12.10 },
        new object[] { "Würzburg", "Würzburg", 49.79, 9.95 },
        new object[] { "Ulm", "Ulm", 48.40, 9.99 },
        new object[] { "Osnabrück", "Osnabrück", 52.28, 8.05 },
        new object[] { "Oldenburg", "Oldenburg", 53.14, 8.21 },
        new object[] { "Göttingen", "Göttingen", 51.54, 9.93 },
        new object[] { "Lübeck", "Lübeck", 53.87, 10.69 },
        new object[] { "Chemnitz", "Chemnitz", 50.83, 12.92 },
        new object[] { "Halle (Saale)", "Halle", 51.48, 11.97 },
        new object[] { "Potsdam", "Potsdam", 52.39, 13.06 },
        new object[] { "Schwerin", "Schwerin", 53.63, 11.41 },
        new object[] { "Koblenz", "Koblenz", 50.36, 7.59 },
        new object[] { "Trier", "Trier", 49.75, 6.64 },
        new object[] { "Siegen", "Siegen", 50.87, 8.02 },
        new object[] { "Paderborn", "Paderborn", 51.72, 8.75 },
        new object[] { "Braunschweig", "Brunswick", 52.27, 10.52 },
        new object[] { "Heidelberg", "Heidelberg", 49.40, 8.67 },
        new object[] { "Ingolstadt", "Ingolstadt", 48.77, 11.43 },
        new object[] { "Passau", "Passau", 48.57, 13.43 },
        new object[] { "Flensburg", "Flensburg", 54.79, 9.44 },
        new object[] { "Jena", "Jena", 50.93, 11.59 },
        new object[] { "Cottbus", "Cottbus", 51.76, 14.33 },
        new object[] { "Konstanz", "Konstanz", 47.66, 9.18 },
        new object[] { "Wien", "Vienna", 48.21, 16.37 },
        new object[] { "Graz", "Graz", 47.07, 15.44 },
        new object[] { "Linz", "Linz", 48.31, 14.29 },
        new object[] { "Salzburg", "Salzburg", 47.81, 13.04 },
        new object[] { "Innsbruck", "Innsbruck", 47.27, 11.39 },
        new object[] { "Zürich", "Zurich", 47.37, 8.54 },
        new object[] { "Bern", "Bern", 46.95, 7.45 },
        new object[] { "Basel", "Basel", 47.56, 7.59 },
        new object[] { "Genf", "Geneva", 46.20, 6.14 },
        new object[] { "Amsterdam", "Amsterdam", 52.37, 4.90 },
        new object[] { "Brüssel", "Brussels", 50.85, 4.35 },
        new object[] { "Luxemburg", "Luxembourg", 49.61, 6.13 },
        new object[] { "Paris", "Paris", 48.86, 2.35 },
        new object[] { "London", "London", 51.51, -0.13 },
        new object[] { "Kopenhagen", "Copenhagen", 55.68, 12.57 },
        new object[] { "Prag", "Prague", 50.08, 14.44 },
        new object[] { "Warschau", "Warsaw", 52.23, 21.01 },
        new object[] { "Mailand", "Milan", 45.46, 9.19 },
        new object[] { "Rom", "Rome", 41.90, 12.50 },
        new object[] { "Madrid", "Madrid", 40.42, -3.70 },
        new object[] { "Stockholm", "Stockholm", 59.33, 18.07 },
        new object[] { "Oslo", "Oslo", 59.91, 10.75 },
    };

    static double Km(double lat1, double lon1, double lat2, double lon2)
    {
        double r = Math.PI / 180;
        double a = Math.Pow(Math.Sin((lat2 - lat1) * r / 2), 2)
                 + Math.Cos(lat1 * r) * Math.Cos(lat2 * r) * Math.Pow(Math.Sin((lon2 - lon1) * r / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(a));
    }

    // "Nähe Köln (22 km)" / "near Cologne (22 km)"; null when nothing is within 150 km
    public static string Near(double lat, double lon)
    {
        object[] best = null; double bestKm = double.MaxValue;
        foreach (var c in Cities)
        {
            double km = Km(lat, lon, (double)c[2], (double)c[3]);
            if (km < bestKm) { bestKm = km; best = c; }
        }
        if (best == null || bestKm > 150) return null;
        string name = (string)(L.De ? best[0] : best[1]);
        return bestKm < 5 ? name : string.Format(L.NearCity, name, Math.Round(bestKm));
    }
}
