using System.Text.Json;
using ElectionSim.Core.Models;

namespace ElectionSim.DataTools.Commands;

/// <summary>
/// Generates a hex cartogram layout for Canada's 343 federal electoral ridings.
/// Three-stage approach:
/// 1. Province shapes are manually defined as blocks of (col, row) hex cells in a pointy-top
///    odd-r hex grid, designed to approximate the geography of electoralcartogram.ca.
///    Each province gets exactly as many cells as it has ridings.
/// 2. Riding centroids (approximate lat/lng) provide geographic ordering data.
/// 3. Greedy assignment maps ridings to hex cells: riding coordinates are normalized to the
///    hex grid's column/row space, then a greedy nearest-neighbor algorithm (assigning outliers
///    first) matches each riding to its closest available cell.
/// </summary>
public static class HexLayoutGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Explicit (col, row) positions for each province, designed to approximate
    // the electoralcartogram.ca layout on a pointy-top odd-r hex grid.
    // Each tuple passed to GenerateBlock is (startCol, endCol, row), expanding to
    // columns startCol..endCol at the given row. Row comments reference approximate
    // latitude mappings (e.g., "56.0N → row 3.0").
    private static readonly Dictionary<string, (int Col, int Row)[]> ProvinceShapes = new()
    {
        // BC (43 ridings): rows 3-13
        ["BC"] = GenerateBlock(
            (3, 3, 3),   // 1 — far north (PG-Peace 56.0N → row 3.0)
            (2, 3, 4),   // 2 — north (Skeena)
            (2, 3, 5),   // 2 — north-central (Cariboo)
            (1, 3, 6),   // 3
            (1, 3, 7),   // 3
            (1, 4, 8),   // 4 — interior (Kamloops → row 10.0)
            (0, 4, 9),   // 5
            (0, 5, 10),  // 6 — Metro Van starts
            (0, 5, 11),  // 6 — Metro Van core (→ row 11.8)
            (0, 5, 12),  // 6 — Metro Van
            (0, 4, 13)   // 5 — Victoria (→ row 13.0)
        ),

        // AB (37 ridings): rows 2-12
        ["AB"] = GenerateBlock(
            (9, 9, 2),    // 1 — Fort McMurray (56.7N → row 2.0)
            (8, 9, 3),    // 2 — Peace River, Grande Prairie
            (8, 9, 4),    // 2 — Lakeland, Yellowhead
            (7, 10, 5),   // 4 — north Edmonton
            (7, 11, 6),   // 5 — Edmonton core (53.5N → row 6.6)
            (7, 11, 7),   // 5 — Edmonton south
            (8, 10, 8),   // 3 — central (Red Deer, Ponoka)
            (7, 11, 9),   // 5 — Calgary north (51.0N → row 10.1)
            (7, 11, 10),  // 5 — Calgary south
            (8, 10, 11),  // 3 — south
            (8, 9, 12)    // 2 — Lethbridge, Med Hat (→ row 12.0)
        ),

        // SK (14 ridings): rows 4-12
        ["SK"] = GenerateBlock(
            (15, 15, 4),   // 1 — Desnethé (55.0N → row 4.0)
            (15, 15, 5),   // 1
            (15, 15, 6),   // 1
            (14, 15, 7),   // 2
            (14, 16, 8),   // 3 — Saskatoon (52.1N → row 8.2)
            (14, 15, 9),   // 2
            (14, 15, 10),  // 2 — Regina (50.5N → row 10.5)
            (15, 15, 11),  // 1
            (15, 15, 12)   // 1 — Souris (→ row 12.0)
        ),

        // MB (14 ridings): rows 3-9
        ["MB"] = GenerateBlock(
            (19, 19, 3),   // 1 — Churchill (56.0N → row 3.0)
            (19, 19, 4),   // 1
            (19, 19, 5),   // 1
            (18, 20, 6),   // 3
            (18, 20, 7),   // 3 — Riding Mtn (→ row 7.7)
            (18, 20, 8),   // 3 — Winnipeg (49.9N → row 8.5)
            (18, 19, 9)    // 2 — Brandon/Provencher (→ row 9.0)
        ),

        // ON (122 ridings): Large block, narrow at top (N. Ontario), wide at bottom (GTA)
        ["ON"] = GenerateBlock(
            (25, 26, 4),   // 2
            (24, 27, 5),   // 4
            (23, 28, 6),   // 6
            (23, 29, 7),   // 7
            (22, 29, 8),   // 8
            (22, 30, 9),   // 9
            (22, 31, 10),  // 10
            (22, 32, 11),  // 11
            (22, 33, 12),  // 12
            (22, 34, 13),  // 13
            (22, 34, 14),  // 13
            (23, 33, 15),  // 11
            (24, 32, 16),  // 9
            (25, 31, 17)   // 7
        ),

        // QC (78 ridings)
        ["QC"] = GenerateBlock(
            (36, 38, 2),   // 3
            (35, 39, 3),   // 5
            (34, 40, 4),   // 7
            (33, 40, 5),   // 8
            (33, 41, 6),   // 9
            (33, 41, 7),   // 9
            (33, 41, 8),   // 9
            (33, 40, 9),   // 8
            (33, 39, 10),  // 7
            (33, 38, 11),  // 6
            (34, 37, 12),  // 4
            (35, 37, 13)   // 3
        ),

        // NL (7 ridings)
        ["NL"] = GenerateBlock(
            (43, 45, 3),  // 3
            (43, 44, 4),  // 2
            (43, 44, 5)   // 2
        ),

        // PE (4 ridings)
        ["PE"] = GenerateBlock(
            (45, 46, 8),  // 2
            (45, 46, 9)   // 2
        ),

        // NB (10 ridings)
        ["NB"] = GenerateBlock(
            (42, 44, 9),   // 3
            (42, 44, 10),  // 3
            (42, 43, 11),  // 2
            (42, 43, 12)   // 2
        ),

        // NS (11 ridings)
        ["NS"] = GenerateBlock(
            (43, 45, 13),  // 3
            (44, 46, 14),  // 3
            (44, 46, 15),  // 3
            (45, 46, 16)   // 2
        ),

        // Territories
        ["YT"] = [(2, 1)],
        ["NT"] = [(7, 1)],
        ["NU"] = [(20, 1)],
    };

    // Approximate centroid coordinates (lat, lng) for each riding.
    // Used to geographically sort ridings within each province's hex shape.
    internal static readonly Dictionary<int, (double Lat, double Lng)> RidingCentroids = new()
    {
        // NL
        [10001] = (47.3, -53.2),   // Avalon
        [10002] = (47.5, -52.6),   // Cape Spear
        [10003] = (48.9, -55.6),   // Central Newfoundland
        [10004] = (53.3, -60.0),   // Labrador
        [10005] = (49.2, -57.5),   // Long Range Mountains
        [10006] = (47.6, -52.7),   // St. John's East
        [10007] = (48.5, -54.0),   // Terra Nova--The Peninsulas

        // PE
        [11001] = (46.2, -62.5),   // Cardigan (east)
        [11002] = (46.2, -63.1),   // Charlottetown (center)
        [11003] = (46.4, -63.8),   // Egmont (west)
        [11004] = (46.4, -63.4),   // Malpeque (north-center)

        // NS
        [12001] = (44.7, -65.5),   // Acadie--Annapolis
        [12002] = (45.8, -61.5),   // Cape Breton--Canso--Antigonish
        [12003] = (45.4, -62.5),   // Central Nova
        [12004] = (45.6, -63.5),   // Cumberland--Colchester
        [12005] = (44.7, -63.5),   // Dartmouth--Cole Harbour
        [12006] = (44.6, -63.6),   // Halifax
        [12007] = (44.7, -63.7),   // Halifax West
        [12008] = (45.0, -64.3),   // Kings--Hants
        [12009] = (44.8, -63.6),   // Sackville--Bedford--Preston
        [12010] = (44.2, -64.5),   // South Shore--St. Margarets
        [12011] = (46.1, -60.2),   // Sydney--Glace Bay

        // NB
        [13001] = (47.6, -65.6),   // Acadie--Bathurst
        [13002] = (46.1, -64.5),   // Beauséjour
        [13003] = (45.9, -66.6),   // Fredericton--Oromocto
        [13004] = (45.5, -65.5),   // Fundy Royal
        [13005] = (47.4, -67.5),   // Madawaska--Restigouche
        [13006] = (46.8, -65.8),   // Miramichi--Grand Lake
        [13007] = (46.1, -64.8),   // Moncton--Dieppe
        [13008] = (45.3, -65.9),   // Saint John--Kennebecasis
        [13009] = (45.4, -66.5),   // Saint John--St. Croix
        [13010] = (46.6, -67.0),   // Tobique--Mactaquac

        // QC
        [24001] = (50.0, -77.0),   // Abitibi--Baie-James--Nunavik--Eeyou
        [24002] = (48.2, -79.0),   // Abitibi--Témiscamingue
        [24003] = (45.55, -73.65), // Ahuntsic-Cartierville
        [24004] = (45.60, -73.60), // Alfred-Pellan (Laval)
        [24005] = (45.6, -74.6),   // Argenteuil--La Petite-Nation
        [24006] = (46.1, -70.8),   // Beauce
        [24007] = (45.3, -74.0),   // Beauharnois--Salaberry--Soulanges
        [24008] = (46.87, -71.20), // Beauport--Limoilou
        [24009] = (46.2, -72.4),   // Bécancour--Nicolet--Saurel
        [24010] = (46.6, -70.8),   // Bellechasse--Les Etchemins--Lévis
        [24011] = (45.50, -73.25), // Beloeil--Chambly
        [24012] = (46.3, -73.2),   // Berthier--Maskinongé
        [24013] = (45.60, -73.58), // Bourassa (Montréal-Nord)
        [24014] = (45.1, -72.5),   // Brome--Missisquoi
        [24015] = (45.47, -73.50), // Brossard--Saint-Lambert
        [24016] = (46.88, -71.30), // Charlesbourg--Haute-Saint-Charles
        [24017] = (45.30, -73.70), // Châteauguay--Les Jardins
        [24018] = (48.40, -71.10), // Chicoutimi--Le Fjord
        [24019] = (45.2, -71.8),   // Compton--Stanstead
        [24020] = (47.5, -69.5),   // Côte-du-Sud--Rivière-du-Loup
        [24021] = (50.2, -66.5),   // Côte-Nord--Kawawachikamach
        [24022] = (45.43, -73.68), // Dorval--Lachine--LaSalle
        [24023] = (45.9, -72.5),   // Drummond
        [24024] = (48.8, -65.0),   // Gaspésie--Les Îles-de-la-Madeleine
        [24025] = (45.48, -75.70), // Gatineau
        [24026] = (45.55, -73.55), // Hochelaga--Rosemont-Est
        [24027] = (45.58, -73.55), // Honoré-Mercier
        [24028] = (45.43, -75.80), // Hull--Aylmer
        [24029] = (46.0, -73.4),   // Joliette--Manawan
        [24030] = (48.40, -71.25), // Jonquière
        [24031] = (45.65, -73.50), // La Pointe-de-l'Île
        [24032] = (45.42, -73.50), // La Prairie--Atateken
        [24033] = (48.5, -72.0),   // Lac-Saint-Jean
        [24034] = (45.43, -73.80), // Lac-Saint-Louis
        [24035] = (45.45, -73.58), // LaSalle--Émard--Verdun
        [24036] = (46.3, -74.6),   // Laurentides--Labelle
        [24037] = (45.53, -73.56), // Laurier--Sainte-Marie
        [24038] = (45.58, -73.72), // Laval--Les Îles
        [24039] = (46.0, -74.2),   // Les Pays-d'en-Haut
        [24040] = (46.7, -71.2),   // Lévis--Lotbinière
        [24041] = (45.48, -73.52), // Longueuil--Charles-LeMoyne
        [24042] = (45.50, -73.42), // Longueuil--Saint-Hubert
        [24043] = (46.82, -71.28), // Louis-Hébert
        [24044] = (46.90, -71.50), // Louis-Saint-Laurent
        [24045] = (45.58, -73.78), // Marc-Aurèle-Fortin (Laval)
        [24046] = (46.1, -71.5),   // Mégantic--L'Érable--Lotbinière
        [24047] = (45.65, -74.10), // Mirabel
        [24048] = (45.50, -73.63), // Mount Royal
        [24049] = (45.52, -73.30), // Mont-Saint-Bruno--L'Acadie
        [24050] = (46.0, -73.7),   // Montcalm
        [24051] = (47.2, -70.8),   // Montmorency--Charlevoix
        [24052] = (45.48, -73.63), // Notre-Dame-de-Grâce--Westmount
        [24053] = (45.52, -73.60), // Outremont
        [24054] = (45.56, -73.58), // Papineau
        [24055] = (45.58, -73.30), // Pierre-Boucher--Les Patriotes
        [24056] = (45.50, -73.82), // Pierrefonds--Dollard
        [24057] = (45.8, -76.2),   // Pontiac--Kitigan Zibi
        [24058] = (46.9, -71.8),   // Portneuf--Jacques-Cartier
        [24059] = (46.82, -71.22), // Québec Centre
        [24060] = (45.70, -73.48), // Repentigny
        [24061] = (45.9, -71.8),   // Richmond--Arthabaska
        [24062] = (48.4, -68.5),   // Rimouski--La Matapédia
        [24063] = (45.60, -73.80), // Rivière-des-Mille-Îles
        [24064] = (45.9, -74.0),   // Rivière-du-Nord
        [24065] = (45.54, -73.58), // Rosemont--La Petite-Patrie
        [24066] = (45.6, -72.8),   // Saint-Hyacinthe--Bagot--Acton
        [24067] = (45.3, -73.3),   // Saint-Jean
        [24068] = (45.50, -73.68), // Saint-Laurent
        [24069] = (45.58, -73.58), // Saint-Léonard--Saint-Michel
        [24070] = (46.5, -72.5),   // Saint-Maurice--Champlain
        [24071] = (45.4, -72.4),   // Shefford
        [24072] = (45.4, -71.9),   // Sherbrooke
        [24073] = (45.68, -73.60), // Terrebonne
        [24074] = (45.68, -73.80), // Thérèse-De Blainville
        [24075] = (46.35, -72.55), // Trois-Rivières
        [24076] = (45.40, -74.03), // Vaudreuil
        [24077] = (45.48, -73.57), // Ville-Marie--Le Sud-Ouest
        [24078] = (45.60, -73.73), // Vimy (Laval)

        // ON
        [35001] = (43.85, -79.05), // Ajax
        [35002] = (45.5, -77.0),   // Algonquin--Renfrew--Pembroke
        [35003] = (43.95, -79.43), // Aurora--Oak Ridges--Richmond Hill
        [35004] = (44.35, -79.68), // Barrie South--Innisfil
        [35005] = (44.42, -79.72), // Barrie--Springwater--Oro-Medonte
        [35006] = (44.2, -77.4),   // Bay of Quinte
        [35007] = (43.68, -79.30), // Beaches--East York
        [35008] = (43.95, -78.75), // Bowmanville--Oshawa North
        [35009] = (43.72, -79.78), // Brampton Centre
        [35010] = (43.72, -79.73), // Brampton--Chinguacousy Park
        [35011] = (43.72, -79.70), // Brampton East
        [35012] = (43.80, -79.80), // Brampton North--Caledon
        [35013] = (43.68, -79.78), // Brampton South
        [35014] = (43.72, -79.83), // Brampton West
        [35015] = (43.15, -80.30), // Brantford--Brant South--Six Nations
        [35016] = (44.6, -80.9),   // Bruce--Grey--Owen Sound
        [35017] = (43.33, -79.80), // Burlington
        [35018] = (43.42, -79.85), // Burlington North--Milton West
        [35019] = (43.38, -80.32), // Cambridge
        [35020] = (45.20, -75.80), // Carleton
        [35021] = (42.30, -82.20), // Chatham-Kent--Leamington
        [35022] = (43.68, -79.43), // Davenport
        [35023] = (43.80, -79.35), // Don Valley North
        [35024] = (43.72, -79.35), // Don Valley West
        [35025] = (43.95, -80.00), // Dufferin--Caledon
        [35026] = (43.72, -79.42), // Eglinton--Lawrence
        [35027] = (42.80, -81.20), // Elgin--St. Thomas--London South
        [35028] = (42.17, -82.95), // Essex
        [35029] = (43.63, -79.53), // Etobicoke Centre
        [35030] = (43.60, -79.52), // Etobicoke--Lakeshore
        [35031] = (43.72, -79.58), // Etobicoke North
        [35032] = (43.22, -80.00), // Flamborough--Glanbrook--Brant North
        [35033] = (43.55, -80.25), // Guelph
        [35034] = (42.90, -80.05), // Haldimand--Norfolk
        [35035] = (44.70, -78.50), // Haliburton--Kawartha Lakes
        [35036] = (43.26, -79.87), // Hamilton Centre
        [35037] = (43.22, -79.78), // Hamilton East--Stoney Creek
        [35038] = (43.23, -79.90), // Hamilton Mountain
        [35039] = (43.25, -80.00), // Hamilton West--Ancaster--Dundas
        [35040] = (44.4, -77.2),   // Hastings--Lennox and Addington
        [35041] = (43.73, -79.52), // Humber River--Black Creek
        [35042] = (44.0, -81.5),   // Huron--Bruce
        [35043] = (45.32, -75.90), // Kanata
        [35044] = (49.0, -82.0),   // Kapuskasing--Timmins--Mushkegowuk
        [35045] = (50.5, -90.0),   // Kenora--Kiiwetinoong
        [35046] = (44.23, -76.50), // Kingston and the Islands
        [35047] = (43.90, -79.55), // King--Vaughan
        [35048] = (43.45, -80.50), // Kitchener Centre
        [35049] = (43.42, -80.48), // Kitchener--Conestoga
        [35050] = (43.40, -80.42), // Kitchener South--Hespeler
        [35051] = (44.8, -76.5),   // Lanark--Frontenac
        [35052] = (44.5, -76.0),   // Leeds--Grenville--Thousand Islands
        [35053] = (43.00, -81.25), // London Centre
        [35054] = (43.02, -81.18), // London--Fanshawe
        [35055] = (43.00, -81.32), // London West
        [35056] = (43.90, -79.28), // Markham--Stouffville
        [35057] = (43.83, -79.38), // Markham--Thornhill
        [35058] = (43.88, -79.30), // Markham--Unionville
        [35059] = (43.10, -81.50), // Middlesex--London
        [35060] = (43.52, -79.88), // Milton East--Halton Hills South
        [35061] = (43.58, -79.63), // Mississauga Centre
        [35062] = (43.58, -79.60), // Mississauga East--Cooksville
        [35063] = (43.55, -79.68), // Mississauga--Erin Mills
        [35064] = (43.52, -79.60), // Mississauga--Lakeshore
        [35065] = (43.68, -79.63), // Mississauga--Malton
        [35066] = (43.58, -79.68), // Mississauga--Streetsville
        [35067] = (45.32, -75.72), // Nepean
        [35068] = (44.05, -79.47), // Newmarket--Aurora
        [35069] = (44.08, -79.58), // New Tecumseth--Gwillimbury
        [35070] = (43.10, -79.10), // Niagara Falls--Niagara-on-the-Lake
        [35071] = (42.90, -79.22), // Niagara South
        [35072] = (43.10, -79.40), // Niagara West
        [35073] = (46.5, -79.5),   // Nipissing--Timiskaming
        [35074] = (44.10, -78.00), // Northumberland--Clarke
        [35075] = (43.42, -79.68), // Oakville East
        [35076] = (43.42, -79.73), // Oakville West
        [35077] = (45.48, -75.50), // Orléans
        [35078] = (43.90, -78.87), // Oshawa
        [35079] = (45.42, -75.70), // Ottawa Centre
        [35080] = (45.38, -75.68), // Ottawa South
        [35081] = (45.42, -75.60), // Ottawa--Vanier--Gloucester
        [35082] = (45.35, -75.78), // Ottawa West--Nepean
        [35083] = (43.10, -80.75), // Oxford
        [35084] = (45.3, -79.5),   // Parry Sound--Muskoka
        [35085] = (43.6, -80.8),   // Perth--Wellington
        [35086] = (44.30, -78.32), // Peterborough
        [35087] = (43.88, -79.10), // Pickering--Brooklin
        [35088] = (45.5, -75.0),   // Prescott--Russell--Cumberland
        [35089] = (43.88, -79.42), // Richmond Hill South
        [35090] = (43.0, -82.0),   // Sarnia--Lambton--Bkejwanong
        [35091] = (46.5, -84.3),   // Sault Ste. Marie--Algoma
        [35092] = (43.80, -79.28), // Scarborough--Agincourt
        [35093] = (43.78, -79.28), // Scarborough Centre--Don Valley East
        [35094] = (43.78, -79.18), // Scarborough--Guildwood--Rouge Park
        [35095] = (43.82, -79.22), // Scarborough North
        [35096] = (43.73, -79.23), // Scarborough Southwest
        [35097] = (43.78, -79.22), // Scarborough--Woburn
        [35098] = (44.30, -80.10), // Simcoe--Grey
        [35099] = (44.60, -79.40), // Simcoe North
        [35100] = (43.63, -79.40), // Spadina--Harbourfront
        [35101] = (43.18, -79.25), // St. Catharines
        [35102] = (45.05, -75.00), // Stormont--Dundas--Glengarry
        [35103] = (46.50, -81.00), // Sudbury
        [35104] = (46.40, -81.00), // Sudbury East--Manitoulin--Nickel Belt
        [35105] = (43.64, -79.45), // Taiaiako'n--Parkdale--High Park
        [35106] = (43.82, -79.42), // Thornhill
        [35107] = (48.4, -89.2),   // Thunder Bay--Rainy River
        [35108] = (48.8, -88.0),   // Thunder Bay--Superior North
        [35109] = (43.66, -79.38), // Toronto Centre
        [35110] = (43.68, -79.33), // Toronto--Danforth
        [35111] = (43.70, -79.42), // Toronto--St. Paul's
        [35112] = (43.67, -79.40), // University--Rosedale
        [35113] = (43.82, -79.53), // Vaughan--Woodbridge
        [35114] = (43.47, -80.52), // Waterloo
        [35115] = (43.75, -80.30), // Wellington--Halton Hills North
        [35116] = (43.88, -78.93), // Whitby
        [35117] = (43.78, -79.42), // Willowdale
        [35118] = (42.30, -82.88), // Windsor--Tecumseh--Lakeshore
        [35119] = (42.28, -83.02), // Windsor West
        [35120] = (43.78, -79.45), // York Centre
        [35121] = (44.00, -79.20), // York--Durham
        [35122] = (43.70, -79.48), // York South--Weston--Etobicoke

        // MB
        [46001] = (49.8, -100.0),  // Brandon--Souris
        [46002] = (56.0, -96.0),   // Churchill--Keewatinook Aski
        [46003] = (49.90, -97.00), // Elmwood--Transcona
        [46004] = (49.93, -97.08), // Kildonan--St. Paul
        [46005] = (49.5, -98.5),   // Portage--Lisgar
        [46006] = (49.3, -96.5),   // Provencher
        [46007] = (50.8, -99.5),   // Riding Mountain
        [46008] = (49.83, -97.10), // St. Boniface--St. Vital
        [46009] = (50.5, -97.0),   // Selkirk--Interlake--Eastman
        [46010] = (49.88, -97.15), // Winnipeg Centre
        [46011] = (49.93, -97.12), // Winnipeg North
        [46012] = (49.83, -97.18), // Winnipeg South
        [46013] = (49.88, -97.15), // Winnipeg South Centre
        [46014] = (49.88, -97.25), // Winnipeg West

        // SK
        [47001] = (53.0, -108.5),  // Battlefords--Lloydminster--Meadow Lake
        [47002] = (52.0, -106.5),  // Carlton Trail--Eagle Creek
        [47003] = (55.0, -106.0),  // Desnethé--Missinippi--Churchill River
        [47004] = (50.5, -105.5),  // Moose Jaw--Lake Centre--Lanigan
        [47005] = (53.2, -105.8),  // Prince Albert
        [47006] = (50.43, -104.68),// Regina--Lewvan
        [47007] = (50.50, -104.00),// Regina--Qu'Appelle
        [47008] = (50.45, -104.60),// Regina--Wascana
        [47009] = (52.08, -106.68),// Saskatoon South
        [47010] = (52.12, -106.63),// Saskatoon--University
        [47011] = (52.15, -106.72),// Saskatoon West
        [47012] = (49.5, -102.5),  // Souris--Moose Mountain
        [47013] = (50.3, -108.0),  // Swift Current--Grasslands--Kindersley
        [47014] = (51.0, -102.5),  // Yorkton--Melville

        // AB
        [48001] = (51.28, -114.10), // Airdrie--Cochrane
        [48002] = (52.0, -112.0),   // Battle River--Crowfoot
        [48003] = (50.5, -113.0),   // Bow River
        [48004] = (51.04, -114.08), // Calgary Centre
        [48005] = (51.08, -114.18), // Calgary Confederation
        [48006] = (51.12, -114.18), // Calgary Crowfoot
        [48007] = (51.04, -113.98), // Calgary East
        [48008] = (50.95, -114.10), // Calgary Heritage
        [48009] = (51.12, -114.02), // Calgary McKnight
        [48010] = (50.92, -114.02), // Calgary Midnapore
        [48011] = (51.12, -114.12), // Calgary Nose Hill
        [48012] = (50.92, -113.92), // Calgary Shepard
        [48013] = (51.02, -114.20), // Calgary Signal Hill
        [48014] = (51.12, -113.98), // Calgary Skyview
        [48015] = (53.54, -113.50), // Edmonton Centre
        [48016] = (53.42, -113.42), // Edmonton Gateway
        [48017] = (53.60, -113.50), // Edmonton Griesbach
        [48018] = (53.62, -113.40), // Edmonton Manning
        [48019] = (53.58, -113.62), // Edmonton Northwest
        [48020] = (53.44, -113.52), // Edmonton Riverbend
        [48021] = (53.48, -113.38), // Edmonton Southeast
        [48022] = (53.52, -113.50), // Edmonton Strathcona
        [48023] = (53.52, -113.60), // Edmonton West
        [48024] = (50.7, -114.0),   // Foothills
        [48025] = (56.7, -111.4),   // Fort McMurray--Cold Lake
        [48026] = (55.2, -118.8),   // Grande Prairie
        [48027] = (54.0, -111.0),   // Lakeland
        [48028] = (53.0, -113.5),   // Leduc--Wetaskiwin
        [48029] = (49.7, -112.8),   // Lethbridge
        [48030] = (49.8, -110.7),   // Medicine Hat--Cardston--Warner
        [48031] = (53.5, -114.5),   // Parkland
        [48032] = (55.8, -117.3),   // Peace River--Westlock
        [48033] = (52.0, -114.0),   // Ponoka--Didsbury
        [48034] = (52.3, -113.8),   // Red Deer
        [48035] = (53.53, -113.30), // Sherwood Park--Fort Saskatchewan
        [48036] = (53.63, -113.62), // St. Albert--Sturgeon River
        [48037] = (53.5, -116.5),   // Yellowhead

        // BC
        [59001] = (49.05, -122.30), // Abbotsford--South Langley
        [59002] = (49.23, -122.95), // Burnaby Central
        [59003] = (49.30, -122.98), // Burnaby North--Seymour
        [59004] = (53.9, -122.8),   // Cariboo--Prince George
        [59005] = (49.17, -121.75), // Chilliwack--Hope
        [59006] = (49.10, -122.72), // Cloverdale--Langley City
        [59007] = (49.5, -116.0),   // Columbia--Kootenay--Southern Rockies
        [59008] = (49.27, -122.80), // Coquitlam--Port Coquitlam
        [59009] = (49.3, -125.0),   // Courtenay--Alberni
        [59010] = (48.60, -123.70), // Cowichan--Malahat--Langford
        [59011] = (49.08, -123.05), // Delta
        [59012] = (48.42, -123.50), // Esquimalt--Saanich--Sooke
        [59013] = (49.13, -122.78), // Fleetwood--Port Kells
        [59014] = (50.7, -119.5),   // Kamloops--Shuswap--Central Rockies
        [59015] = (50.4, -120.3),   // Kamloops--Thompson--Nicola
        [59016] = (49.88, -119.48), // Kelowna
        [59017] = (49.10, -122.58), // Langley Township--Fraser Heights
        [59018] = (49.13, -122.30), // Mission--Matsqui--Abbotsford
        [59019] = (49.17, -123.93), // Nanaimo--Ladysmith
        [59020] = (49.22, -122.90), // New Westminster--Burnaby--Maillardville
        [59021] = (50.0, -125.5),   // North Island--Powell River
        [59022] = (49.33, -123.12), // North Vancouver--Capilano
        [59023] = (49.72, -119.60), // Okanagan Lake West--South Kelowna
        [59024] = (49.22, -122.60), // Pitt Meadows--Maple Ridge
        [59025] = (49.28, -122.82), // Port Moody--Coquitlam
        [59026] = (56.0, -122.0),   // Prince George--Peace River--Northern Rockies
        [59027] = (49.18, -123.13), // Richmond Centre--Marpole
        [59028] = (49.15, -123.03), // Richmond East--Steveston
        [59029] = (48.52, -123.38), // Saanich--Gulf Islands
        [59030] = (49.3, -118.0),   // Similkameen--South Okanagan--West Kootenay
        [59031] = (54.8, -127.2),   // Skeena--Bulkley Valley
        [59032] = (49.02, -122.82), // South Surrey--White Rock
        [59033] = (49.18, -122.82), // Surrey Centre
        [59034] = (49.12, -122.85), // Surrey Newton
        [59035] = (49.27, -123.12), // Vancouver Centre
        [59036] = (49.28, -123.07), // Vancouver East
        [59037] = (49.22, -123.02), // Vancouver Fraserview--South Burnaby
        [59038] = (49.26, -123.15), // Vancouver Granville
        [59039] = (49.24, -123.08), // Vancouver Kingsway
        [59040] = (49.27, -123.15), // Vancouver Quadra
        [59041] = (50.3, -119.3),   // Vernon--Lake Country--Monashee
        [59042] = (48.43, -123.37), // Victoria
        [59043] = (49.50, -123.20), // West Vancouver--Sunshine Coast--Sea to Sky

        // Territories (single hex each, no matching needed)
        [60001] = (63.0, -135.0),  // Yukon
        [61001] = (64.0, -120.0),  // Northwest Territories
        [62001] = (66.0, -86.0),   // Nunavut
    };

    /// <summary>
    /// Expands a compact row-range specification into individual (col, row) hex positions.
    /// Each input tuple specifies a horizontal run of hex cells from startCol to endCol
    /// at a given row — a convenience for defining province shapes without listing every cell.
    /// </summary>
    private static (int Col, int Row)[] GenerateBlock(params (int StartCol, int EndCol, int Row)[] rows)
    {
        var positions = new List<(int Col, int Row)>();
        foreach (var (startCol, endCol, row) in rows)
        {
            for (int c = startCol; c <= endCol; c++)
            {
                positions.Add((c, row));
            }
        }
        return positions.ToArray();
    }

    /// <summary>
    /// Assigns ridings to hex positions using geographic proximity matching.
    /// Maps riding lat/lng to the hex grid coordinate space, then uses greedy
    /// nearest-neighbor assignment.
    /// </summary>
    private static List<(int RidingId, int Col, int Row)> AssignRidingsToPositions(
        List<Riding> ridings, (int Col, int Row)[] positions)
    {
        if (ridings.Count == 1)
            return [(ridings[0].Id, positions[0].Col, positions[0].Row)];

        // Get centroids for these ridings
        var ridingsWithCoords = ridings
            .Select(r => (
                Riding: r,
                Coord: RidingCentroids.GetValueOrDefault(r.Id, (0, 0))
            ))
            .ToList();

        // Find lat/lng bounds
        double minLat = ridingsWithCoords.Min(r => r.Coord.Lat);
        double maxLat = ridingsWithCoords.Max(r => r.Coord.Lat);
        double minLng = ridingsWithCoords.Min(r => r.Coord.Lng);
        double maxLng = ridingsWithCoords.Max(r => r.Coord.Lng);
        double latRange = maxLat - minLat;
        double lngRange = maxLng - minLng;
        if (latRange < 0.01) latRange = 1;
        if (lngRange < 0.01) lngRange = 1;

        // Find hex position bounds
        int minCol = positions.Min(p => p.Col);
        int maxCol = positions.Max(p => p.Col);
        int minRow = positions.Min(p => p.Row);
        int maxRow = positions.Max(p => p.Row);
        double colRange = maxCol - minCol;
        double rowRange = maxRow - minRow;
        if (colRange < 0.01) colRange = 1;
        if (rowRange < 0.01) rowRange = 1;

        // Map each riding's lat/lng to a target position in hex grid space.
        // Longitude → column: more negative (farther west) = lower column number.
        // Latitude → row (inverted): higher latitude (farther north) = lower row number,
        // since row 0 is at the top of the grid.
        var ridingTargets = ridingsWithCoords.Select(r =>
        {
            double targetCol = minCol + (r.Coord.Lng - minLng) / lngRange * colRange;
            double targetRow = minRow + (maxLat - r.Coord.Lat) / latRange * rowRange;
            return (r.Riding, TargetCol: targetCol, TargetRow: targetRow);
        }).ToList();

        // Greedy nearest-neighbor assignment
        var available = new HashSet<int>(Enumerable.Range(0, positions.Length));
        var result = new List<(int RidingId, int Col, int Row)>();

        // Assign outliers first: if center ridings were assigned first, they'd claim the best
        // center positions and push peripheral ridings to whatever remains. By assigning the most
        // geographically distant ridings first, they get positions near their natural edge, while
        // interior ridings (which have more nearby options) are assigned last when it matters less.
        double centerCol = (minCol + maxCol) / 2.0;
        double centerRow = (minRow + maxRow) / 2.0;
        var sortedRidings = ridingTargets
            .OrderByDescending(r =>
            {
                double dc = r.TargetCol - centerCol;
                double dr = r.TargetRow - centerRow;
                return dc * dc + dr * dr;
            })
            .ToList();

        foreach (var riding in sortedRidings)
        {
            int bestIdx = -1;
            double bestDist = double.MaxValue;

            foreach (int idx in available)
            {
                double dc = positions[idx].Col - riding.TargetCol;
                double dr = positions[idx].Row - riding.TargetRow;
                double dist = dc * dc + dr * dr;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = idx;
                }
            }

            available.Remove(bestIdx);
            result.Add((riding.Riding.Id, positions[bestIdx].Col, positions[bestIdx].Row));
        }

        return result;
    }

    public static async Task RunAsync(string processedDir, string wwwrootDataDir)
    {
        Console.WriteLine("Generating hex cartogram layout...");

        // Load ridings
        var ridingsPath = Path.Combine(processedDir, "ridings.json");
        if (!File.Exists(ridingsPath))
        {
            Console.WriteLine($"Error: {ridingsPath} not found. Run generate-sample or process first.");
            return;
        }

        var ridingsJson = await File.ReadAllTextAsync(ridingsPath);
        var ridings = JsonSerializer.Deserialize<List<Riding>>(ridingsJson, JsonOptions);
        if (ridings == null || ridings.Count == 0)
        {
            Console.WriteLine("Error: No ridings found.");
            return;
        }

        // Group ridings by province
        var byProvince = ridings.GroupBy(r => GetProvinceAbbr(r.Province))
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id).ToList());

        var hexPositions = new List<HexPosition>();

        foreach (var (abbr, provinceRidings) in byProvince)
        {
            if (!ProvinceShapes.TryGetValue(abbr, out var shape))
            {
                Console.WriteLine($"  Warning: No hex shape defined for province '{abbr}', skipping {provinceRidings.Count} ridings.");
                continue;
            }

            if (shape.Length < provinceRidings.Count)
            {
                Console.WriteLine($"  Warning: Province '{abbr}' has {provinceRidings.Count} ridings but only {shape.Length} hex positions. Extra ridings will be skipped.");
            }

            var assignments = AssignRidingsToPositions(
                provinceRidings.Take(shape.Length).ToList(), shape);

            foreach (var (ridingId, col, row) in assignments)
            {
                hexPositions.Add(new HexPosition(ridingId, col, row));
            }
        }

        // Write output
        var json = JsonSerializer.Serialize(hexPositions, JsonOptions);

        var processedPath = Path.Combine(processedDir, "hex-layout.json");
        await File.WriteAllTextAsync(processedPath, json);

        var wwwrootPath = Path.Combine(wwwrootDataDir, "hex-layout.json");
        await File.WriteAllTextAsync(wwwrootPath, json);

        Console.WriteLine($"  Written hex-layout.json ({hexPositions.Count} positions)");
        Console.WriteLine("Hex layout generation complete.");
    }

    private static string GetProvinceAbbr(string province) => province switch
    {
        "Newfoundland and Labrador" => "NL",
        "Prince Edward Island" => "PE",
        "Nova Scotia" => "NS",
        "New Brunswick" => "NB",
        "Quebec" => "QC",
        "Ontario" => "ON",
        "Manitoba" => "MB",
        "Saskatchewan" => "SK",
        "Alberta" => "AB",
        "British Columbia" => "BC",
        "Yukon" => "YT",
        "Northwest Territories" => "NT",
        "Nunavut" => "NU",
        _ => "??"
    };
}
