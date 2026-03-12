using System.Text.Json;
using ElectionSim.Core.Models;

namespace ElectionSim.DataTools.Commands;

/// <summary>
/// Generates synthetic riding and election result data for development without requiring
/// real Elections Canada CSV files. Outputs to data/processed/ and wwwroot/data/.
/// </summary>
public static class SampleDataGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Approximate 2025 Canadian federal election results by region
    // National: LPC 43.8%, CPC 41.3%, NDP 6.3%, BQ 6.3% (QC only ~27.7%), GPC 1.6%, PPC 0.7%
    private static readonly Dictionary<Region, Dictionary<Party, double>> Results2025 = new()
    {
        [Region.Atlantic] = new() {
            [Party.LPC] = 0.52, [Party.CPC] = 0.28, [Party.NDP] = 0.13,
            [Party.BQ] = 0.00, [Party.GPC] = 0.04, [Party.PPC] = 0.01 },
        [Region.Quebec] = new() {
            [Party.LPC] = 0.42, [Party.CPC] = 0.20, [Party.NDP] = 0.05,
            [Party.BQ] = 0.28, [Party.GPC] = 0.02, [Party.PPC] = 0.01 },
        [Region.Ontario] = new() {
            [Party.LPC] = 0.48, [Party.CPC] = 0.38, [Party.NDP] = 0.07,
            [Party.BQ] = 0.00, [Party.GPC] = 0.03, [Party.PPC] = 0.01 },
        [Region.Prairies] = new() {
            [Party.LPC] = 0.32, [Party.CPC] = 0.50, [Party.NDP] = 0.12,
            [Party.BQ] = 0.00, [Party.GPC] = 0.02, [Party.PPC] = 0.02 },
        [Region.Alberta] = new() {
            [Party.LPC] = 0.24, [Party.CPC] = 0.63, [Party.NDP] = 0.06,
            [Party.BQ] = 0.00, [Party.GPC] = 0.02, [Party.PPC] = 0.03 },
        [Region.BritishColumbia] = new() {
            [Party.LPC] = 0.42, [Party.CPC] = 0.37, [Party.NDP] = 0.13,
            [Party.BQ] = 0.00, [Party.GPC] = 0.03, [Party.PPC] = 0.01 },
        [Region.North] = new() {
            [Party.LPC] = 0.42, [Party.CPC] = 0.22, [Party.NDP] = 0.28,
            [Party.BQ] = 0.00, [Party.GPC] = 0.04, [Party.PPC] = 0.01 },
    };

    // Province data with riding counts (343 total for 2025 redistribution)
    private static readonly (string Province, string Abbr, Region Region, int Ridings)[] Provinces =
    [
        ("Newfoundland and Labrador", "NL", Region.Atlantic, 7),
        ("Prince Edward Island", "PE", Region.Atlantic, 4),
        ("Nova Scotia", "NS", Region.Atlantic, 11),
        ("New Brunswick", "NB", Region.Atlantic, 10),
        ("Quebec", "QC", Region.Quebec, 78),
        ("Ontario", "ON", Region.Ontario, 122),
        ("Manitoba", "MB", Region.Prairies, 14),
        ("Saskatchewan", "SK", Region.Prairies, 14),
        ("Alberta", "AB", Region.Alberta, 37),
        ("British Columbia", "BC", Region.BritishColumbia, 43),
        ("Yukon", "YT", Region.North, 1),
        ("Northwest Territories", "NT", Region.North, 1),
        ("Nunavut", "NU", Region.North, 1),
    ];

    // Actual federal electoral district names from the 2022 Representation Order (343 ridings)
    // Source: Elections Canada https://www.elections.ca/content.aspx?section=res&dir=cir/red/343list&document=index
    private static readonly Dictionary<string, (string Name, string NameFr)[]> RidingNames = new()
    {
        ["NL"] =
        [
            ("Avalon", "Avalon"),
            ("Cape Spear", "Cape Spear"),
            ("Central Newfoundland", "Central Newfoundland"),
            ("Labrador", "Labrador"),
            ("Long Range Mountains", "Long Range Mountains"),
            ("St. John's East", "St. John's-Est"),
            ("Terra Nova\u2014The Peninsulas", "Terra Nova\u2014Les P\u00e9ninsules"),
        ],
        ["PE"] =
        [
            ("Cardigan", "Cardigan"),
            ("Charlottetown", "Charlottetown"),
            ("Egmont", "Egmont"),
            ("Malpeque", "Malpeque"),
        ],
        ["NS"] =
        [
            ("Acadie\u2014Annapolis", "Acadie\u2014Annapolis"),
            ("Cape Breton\u2014Canso\u2014Antigonish", "Cape Breton\u2014Canso\u2014Antigonish"),
            ("Central Nova", "Nova-Centre"),
            ("Cumberland\u2014Colchester", "Cumberland\u2014Colchester"),
            ("Dartmouth\u2014Cole Harbour", "Dartmouth\u2014Cole Harbour"),
            ("Halifax", "Halifax"),
            ("Halifax West", "Halifax-Ouest"),
            ("Kings\u2014Hants", "Kings\u2014Hants"),
            ("Sackville\u2014Bedford\u2014Preston", "Sackville\u2014Bedford\u2014Preston"),
            ("South Shore\u2014St. Margarets", "South Shore\u2014St. Margarets"),
            ("Sydney\u2014Glace Bay", "Sydney\u2014Glace Bay"),
        ],
        ["NB"] =
        [
            ("Acadie\u2014Bathurst", "Acadie\u2014Bathurst"),
            ("Beaus\u00e9jour", "Beaus\u00e9jour"),
            ("Fredericton\u2014Oromocto", "Fredericton\u2014Oromocto"),
            ("Fundy Royal", "Fundy Royal"),
            ("Madawaska\u2014Restigouche", "Madawaska\u2014Restigouche"),
            ("Miramichi\u2014Grand Lake", "Miramichi\u2014Grand Lake"),
            ("Moncton\u2014Dieppe", "Moncton\u2014Dieppe"),
            ("Saint John\u2014Kennebecasis", "Saint John\u2014Kennebecasis"),
            ("Saint John\u2014St. Croix", "Saint John\u2014St. Croix"),
            ("Tobique\u2014Mactaquac", "Tobique\u2014Mactaquac"),
        ],
        ["QC"] =
        [
            ("Abitibi\u2014Baie-James\u2014Nunavik\u2014Eeyou", "Abitibi\u2014Baie-James\u2014Nunavik\u2014Eeyou"),
            ("Abitibi\u2014T\u00e9miscamingue", "Abitibi\u2014T\u00e9miscamingue"),
            ("Ahuntsic-Cartierville", "Ahuntsic-Cartierville"),
            ("Alfred-Pellan", "Alfred-Pellan"),
            ("Argenteuil\u2014La Petite-Nation", "Argenteuil\u2014La Petite-Nation"),
            ("Beauce", "Beauce"),
            ("Beauharnois\u2014Salaberry\u2014Soulanges\u2014Huntingdon", "Beauharnois\u2014Salaberry\u2014Soulanges\u2014Huntingdon"),
            ("Beauport\u2014Limoilou", "Beauport\u2014Limoilou"),
            ("B\u00e9cancour\u2014Nicolet\u2014Saurel\u2014Aln\u00f4bak", "B\u00e9cancour\u2014Nicolet\u2014Saurel\u2014Aln\u00f4bak"),
            ("Bellechasse\u2014Les Etchemins\u2014L\u00e9vis", "Bellechasse\u2014Les Etchemins\u2014L\u00e9vis"),
            ("Beloeil\u2014Chambly", "Beloeil\u2014Chambly"),
            ("Berthier\u2014Maskinong\u00e9", "Berthier\u2014Maskinong\u00e9"),
            ("Bourassa", "Bourassa"),
            ("Brome\u2014Missisquoi", "Brome\u2014Missisquoi"),
            ("Brossard\u2014Saint-Lambert", "Brossard\u2014Saint-Lambert"),
            ("Charlesbourg\u2014Haute-Saint-Charles", "Charlesbourg\u2014Haute-Saint-Charles"),
            ("Ch\u00e2teauguay\u2014Les Jardins-de-Napierville", "Ch\u00e2teauguay\u2014Les Jardins-de-Napierville"),
            ("Chicoutimi\u2014Le Fjord", "Chicoutimi\u2014Le Fjord"),
            ("Compton\u2014Stanstead", "Compton\u2014Stanstead"),
            ("C\u00f4te-du-Sud\u2014Rivi\u00e8re-du-Loup\u2014Kataskomiq\u2014T\u00e9miscouata", "C\u00f4te-du-Sud\u2014Rivi\u00e8re-du-Loup\u2014Kataskomiq\u2014T\u00e9miscouata"),
            ("C\u00f4te-Nord\u2014Kawawachikamach\u2014Nitassinan", "C\u00f4te-Nord\u2014Kawawachikamach\u2014Nitassinan"),
            ("Dorval\u2014Lachine\u2014LaSalle", "Dorval\u2014Lachine\u2014LaSalle"),
            ("Drummond", "Drummond"),
            ("Gasp\u00e9sie\u2014Les \u00celes-de-la-Madeleine\u2014Listuguj", "Gasp\u00e9sie\u2014Les \u00celes-de-la-Madeleine\u2014Listuguj"),
            ("Gatineau", "Gatineau"),
            ("Hochelaga\u2014Rosemont-Est", "Hochelaga\u2014Rosemont-Est"),
            ("Honor\u00e9-Mercier", "Honor\u00e9-Mercier"),
            ("Hull\u2014Aylmer", "Hull\u2014Aylmer"),
            ("Joliette\u2014Manawan", "Joliette\u2014Manawan"),
            ("Jonqui\u00e8re", "Jonqui\u00e8re"),
            ("La Pointe-de-l'\u00cele", "La Pointe-de-l'\u00cele"),
            ("La Prairie\u2014Atateken", "La Prairie\u2014Atateken"),
            ("Lac-Saint-Jean", "Lac-Saint-Jean"),
            ("Lac-Saint-Louis", "Lac-Saint-Louis"),
            ("LaSalle\u2014\u00c9mard\u2014Verdun", "LaSalle\u2014\u00c9mard\u2014Verdun"),
            ("Laurentides\u2014Labelle", "Laurentides\u2014Labelle"),
            ("Laurier\u2014Sainte-Marie", "Laurier\u2014Sainte-Marie"),
            ("Laval\u2014Les \u00celes", "Laval\u2014Les \u00celes"),
            ("Les Pays-d'en-Haut", "Les Pays-d'en-Haut"),
            ("L\u00e9vis\u2014Lotbini\u00e8re", "L\u00e9vis\u2014Lotbini\u00e8re"),
            ("Longueuil\u2014Charles-LeMoyne", "Longueuil\u2014Charles-LeMoyne"),
            ("Longueuil\u2014Saint-Hubert", "Longueuil\u2014Saint-Hubert"),
            ("Louis-H\u00e9bert", "Louis-H\u00e9bert"),
            ("Louis-Saint-Laurent\u2014Akiawenhrahk", "Louis-Saint-Laurent\u2014Akiawenhrahk"),
            ("Marc-Aur\u00e8le-Fortin", "Marc-Aur\u00e8le-Fortin"),
            ("M\u00e9gantic\u2014L'\u00c9rable\u2014Lotbini\u00e8re", "M\u00e9gantic\u2014L'\u00c9rable\u2014Lotbini\u00e8re"),
            ("Mirabel", "Mirabel"),
            ("Mount Royal", "Mont-Royal"),
            ("Mont-Saint-Bruno\u2014L'Acadie", "Mont-Saint-Bruno\u2014L'Acadie"),
            ("Montcalm", "Montcalm"),
            ("Montmorency\u2014Charlevoix", "Montmorency\u2014Charlevoix"),
            ("Notre-Dame-de-Gr\u00e2ce\u2014Westmount", "Notre-Dame-de-Gr\u00e2ce\u2014Westmount"),
            ("Outremont", "Outremont"),
            ("Papineau", "Papineau"),
            ("Pierre-Boucher\u2014Les Patriotes\u2014Verch\u00e8res", "Pierre-Boucher\u2014Les Patriotes\u2014Verch\u00e8res"),
            ("Pierrefonds\u2014Dollard", "Pierrefonds\u2014Dollard"),
            ("Pontiac\u2014Kitigan Zibi", "Pontiac\u2014Kitigan Zibi"),
            ("Portneuf\u2014Jacques-Cartier", "Portneuf\u2014Jacques-Cartier"),
            ("Qu\u00e9bec Centre", "Qu\u00e9bec-Centre"),
            ("Repentigny", "Repentigny"),
            ("Richmond\u2014Arthabaska", "Richmond\u2014Arthabaska"),
            ("Rimouski\u2014La Matap\u00e9dia", "Rimouski\u2014La Matap\u00e9dia"),
            ("Rivi\u00e8re-des-Mille-\u00celes", "Rivi\u00e8re-des-Mille-\u00celes"),
            ("Rivi\u00e8re-du-Nord", "Rivi\u00e8re-du-Nord"),
            ("Rosemont\u2014La Petite-Patrie", "Rosemont\u2014La Petite-Patrie"),
            ("Saint-Hyacinthe\u2014Bagot\u2014Acton", "Saint-Hyacinthe\u2014Bagot\u2014Acton"),
            ("Saint-Jean", "Saint-Jean"),
            ("Saint-Laurent", "Saint-Laurent"),
            ("Saint-L\u00e9onard\u2014Saint-Michel", "Saint-L\u00e9onard\u2014Saint-Michel"),
            ("Saint-Maurice\u2014Champlain", "Saint-Maurice\u2014Champlain"),
            ("Shefford", "Shefford"),
            ("Sherbrooke", "Sherbrooke"),
            ("Terrebonne", "Terrebonne"),
            ("Th\u00e9r\u00e8se-De Blainville", "Th\u00e9r\u00e8se-De Blainville"),
            ("Trois-Rivi\u00e8res", "Trois-Rivi\u00e8res"),
            ("Vaudreuil", "Vaudreuil"),
            ("Ville-Marie\u2014Le Sud-Ouest\u2014\u00cele-des-S\u0153urs", "Ville-Marie\u2014Le Sud-Ouest\u2014\u00cele-des-S\u0153urs"),
            ("Vimy", "Vimy"),
        ],
        ["ON"] =
        [
            ("Ajax", "Ajax"),
            ("Algonquin\u2014Renfrew\u2014Pembroke", "Algonquin\u2014Renfrew\u2014Pembroke"),
            ("Aurora\u2014Oak Ridges\u2014Richmond Hill", "Aurora\u2014Oak Ridges\u2014Richmond Hill"),
            ("Barrie South\u2014Innisfil", "Barrie-Sud\u2014Innisfil"),
            ("Barrie\u2014Springwater\u2014Oro-Medonte", "Barrie\u2014Springwater\u2014Oro-Medonte"),
            ("Bay of Quinte", "Bay of Quinte"),
            ("Beaches\u2014East York", "Beaches\u2014East York"),
            ("Bowmanville\u2014Oshawa North", "Bowmanville\u2014Oshawa-Nord"),
            ("Brampton Centre", "Brampton-Centre"),
            ("Brampton\u2014Chinguacousy Park", "Brampton\u2014Chinguacousy Park"),
            ("Brampton East", "Brampton-Est"),
            ("Brampton North\u2014Caledon", "Brampton-Nord\u2014Caledon"),
            ("Brampton South", "Brampton-Sud"),
            ("Brampton West", "Brampton-Ouest"),
            ("Brantford\u2014Brant South\u2014Six Nations", "Brantford\u2014Brant-Sud\u2014Six Nations"),
            ("Bruce\u2014Grey\u2014Owen Sound", "Bruce\u2014Grey\u2014Owen Sound"),
            ("Burlington", "Burlington"),
            ("Burlington North\u2014Milton West", "Burlington-Nord\u2014Milton-Ouest"),
            ("Cambridge", "Cambridge"),
            ("Carleton", "Carleton"),
            ("Chatham-Kent\u2014Leamington", "Chatham-Kent\u2014Leamington"),
            ("Davenport", "Davenport"),
            ("Don Valley North", "Don Valley-Nord"),
            ("Don Valley West", "Don Valley-Ouest"),
            ("Dufferin\u2014Caledon", "Dufferin\u2014Caledon"),
            ("Eglinton\u2014Lawrence", "Eglinton\u2014Lawrence"),
            ("Elgin\u2014St. Thomas\u2014London South", "Elgin\u2014St. Thomas\u2014London-Sud"),
            ("Essex", "Essex"),
            ("Etobicoke Centre", "Etobicoke-Centre"),
            ("Etobicoke\u2014Lakeshore", "Etobicoke\u2014Lakeshore"),
            ("Etobicoke North", "Etobicoke-Nord"),
            ("Flamborough\u2014Glanbrook\u2014Brant North", "Flamborough\u2014Glanbrook\u2014Brant-Nord"),
            ("Guelph", "Guelph"),
            ("Haldimand\u2014Norfolk", "Haldimand\u2014Norfolk"),
            ("Haliburton\u2014Kawartha Lakes", "Haliburton\u2014Kawartha Lakes"),
            ("Hamilton Centre", "Hamilton-Centre"),
            ("Hamilton East\u2014Stoney Creek", "Hamilton-Est\u2014Stoney Creek"),
            ("Hamilton Mountain", "Hamilton Mountain"),
            ("Hamilton West\u2014Ancaster\u2014Dundas", "Hamilton-Ouest\u2014Ancaster\u2014Dundas"),
            ("Hastings\u2014Lennox and Addington\u2014Tyendinaga", "Hastings\u2014Lennox and Addington\u2014Tyendinaga"),
            ("Humber River\u2014Black Creek", "Humber River\u2014Black Creek"),
            ("Huron\u2014Bruce", "Huron\u2014Bruce"),
            ("Kanata", "Kanata"),
            ("Kapuskasing\u2014Timmins\u2014Mushkegowuk", "Kapuskasing\u2014Timmins\u2014Mushkegowuk"),
            ("Kenora\u2014Kiiwetinoong", "Kenora\u2014Kiiwetinoong"),
            ("Kingston and the Islands", "Kingston et les \u00celes"),
            ("King\u2014Vaughan", "King\u2014Vaughan"),
            ("Kitchener Centre", "Kitchener-Centre"),
            ("Kitchener\u2014Conestoga", "Kitchener\u2014Conestoga"),
            ("Kitchener South\u2014Hespeler", "Kitchener-Sud\u2014Hespeler"),
            ("Lanark\u2014Frontenac", "Lanark\u2014Frontenac"),
            ("Leeds\u2014Grenville\u2014Thousand Islands\u2014Rideau Lakes", "Leeds\u2014Grenville\u2014Thousand Islands\u2014Rideau Lakes"),
            ("London Centre", "London-Centre"),
            ("London\u2014Fanshawe", "London\u2014Fanshawe"),
            ("London West", "London-Ouest"),
            ("Markham\u2014Stouffville", "Markham\u2014Stouffville"),
            ("Markham\u2014Thornhill", "Markham\u2014Thornhill"),
            ("Markham\u2014Unionville", "Markham\u2014Unionville"),
            ("Middlesex\u2014London", "Middlesex\u2014London"),
            ("Milton East\u2014Halton Hills South", "Milton-Est\u2014Halton Hills-Sud"),
            ("Mississauga Centre", "Mississauga-Centre"),
            ("Mississauga East\u2014Cooksville", "Mississauga-Est\u2014Cooksville"),
            ("Mississauga\u2014Erin Mills", "Mississauga\u2014Erin Mills"),
            ("Mississauga\u2014Lakeshore", "Mississauga\u2014Lakeshore"),
            ("Mississauga\u2014Malton", "Mississauga\u2014Malton"),
            ("Mississauga\u2014Streetsville", "Mississauga\u2014Streetsville"),
            ("Nepean", "Nepean"),
            ("Newmarket\u2014Aurora", "Newmarket\u2014Aurora"),
            ("New Tecumseth\u2014Gwillimbury", "New Tecumseth\u2014Gwillimbury"),
            ("Niagara Falls\u2014Niagara-on-the-Lake", "Niagara Falls\u2014Niagara-on-the-Lake"),
            ("Niagara South", "Niagara-Sud"),
            ("Niagara West", "Niagara-Ouest"),
            ("Nipissing\u2014Timiskaming", "Nipissing\u2014Timiskaming"),
            ("Northumberland\u2014Clarke", "Northumberland\u2014Clarke"),
            ("Oakville East", "Oakville-Est"),
            ("Oakville West", "Oakville-Ouest"),
            ("Orl\u00e9ans", "Orl\u00e9ans"),
            ("Oshawa", "Oshawa"),
            ("Ottawa Centre", "Ottawa-Centre"),
            ("Ottawa South", "Ottawa-Sud"),
            ("Ottawa\u2014Vanier\u2014Gloucester", "Ottawa\u2014Vanier\u2014Gloucester"),
            ("Ottawa West\u2014Nepean", "Ottawa-Ouest\u2014Nepean"),
            ("Oxford", "Oxford"),
            ("Parry Sound\u2014Muskoka", "Parry Sound\u2014Muskoka"),
            ("Perth\u2014Wellington", "Perth\u2014Wellington"),
            ("Peterborough", "Peterborough"),
            ("Pickering\u2014Brooklin", "Pickering\u2014Brooklin"),
            ("Prescott\u2014Russell\u2014Cumberland", "Prescott\u2014Russell\u2014Cumberland"),
            ("Richmond Hill South", "Richmond Hill-Sud"),
            ("Sarnia\u2014Lambton\u2014Bkejwanong", "Sarnia\u2014Lambton\u2014Bkejwanong"),
            ("Sault Ste. Marie\u2014Algoma", "Sault Ste. Marie\u2014Algoma"),
            ("Scarborough\u2014Agincourt", "Scarborough\u2014Agincourt"),
            ("Scarborough Centre\u2014Don Valley East", "Scarborough-Centre\u2014Don Valley-Est"),
            ("Scarborough\u2014Guildwood\u2014Rouge Park", "Scarborough\u2014Guildwood\u2014Rouge Park"),
            ("Scarborough North", "Scarborough-Nord"),
            ("Scarborough Southwest", "Scarborough-Sud-Ouest"),
            ("Scarborough\u2014Woburn", "Scarborough\u2014Woburn"),
            ("Simcoe\u2014Grey", "Simcoe\u2014Grey"),
            ("Simcoe North", "Simcoe-Nord"),
            ("Spadina\u2014Harbourfront", "Spadina\u2014Harbourfront"),
            ("St. Catharines", "St. Catharines"),
            ("Stormont\u2014Dundas\u2014Glengarry", "Stormont\u2014Dundas\u2014Glengarry"),
            ("Sudbury", "Sudbury"),
            ("Sudbury East\u2014Manitoulin\u2014Nickel Belt", "Sudbury-Est\u2014Manitoulin\u2014Nickel Belt"),
            ("Taiaiako'n\u2014Parkdale\u2014High Park", "Taiaiako'n\u2014Parkdale\u2014High Park"),
            ("Thornhill", "Thornhill"),
            ("Thunder Bay\u2014Rainy River", "Thunder Bay\u2014Rainy River"),
            ("Thunder Bay\u2014Superior North", "Thunder Bay\u2014Sup\u00e9rieur-Nord"),
            ("Toronto Centre", "Toronto-Centre"),
            ("Toronto\u2014Danforth", "Toronto\u2014Danforth"),
            ("Toronto\u2014St. Paul's", "Toronto\u2014St. Paul's"),
            ("University\u2014Rosedale", "University\u2014Rosedale"),
            ("Vaughan\u2014Woodbridge", "Vaughan\u2014Woodbridge"),
            ("Waterloo", "Waterloo"),
            ("Wellington\u2014Halton Hills North", "Wellington\u2014Halton Hills-Nord"),
            ("Whitby", "Whitby"),
            ("Willowdale", "Willowdale"),
            ("Windsor\u2014Tecumseh\u2014Lakeshore", "Windsor\u2014Tecumseh\u2014Lakeshore"),
            ("Windsor West", "Windsor-Ouest"),
            ("York Centre", "York-Centre"),
            ("York\u2014Durham", "York\u2014Durham"),
            ("York South\u2014Weston\u2014Etobicoke", "York-Sud\u2014Weston\u2014Etobicoke"),
        ],
        ["MB"] =
        [
            ("Brandon\u2014Souris", "Brandon\u2014Souris"),
            ("Churchill\u2014Keewatinook Aski", "Churchill\u2014Keewatinook Aski"),
            ("Elmwood\u2014Transcona", "Elmwood\u2014Transcona"),
            ("Kildonan\u2014St. Paul", "Kildonan\u2014St. Paul"),
            ("Portage\u2014Lisgar", "Portage\u2014Lisgar"),
            ("Provencher", "Provencher"),
            ("Riding Mountain", "Mont-Riding"),
            ("St. Boniface\u2014St. Vital", "Saint-Boniface\u2014Saint-Vital"),
            ("Selkirk\u2014Interlake\u2014Eastman", "Selkirk\u2014Interlake\u2014Eastman"),
            ("Winnipeg Centre", "Winnipeg-Centre"),
            ("Winnipeg North", "Winnipeg-Nord"),
            ("Winnipeg South", "Winnipeg-Sud"),
            ("Winnipeg South Centre", "Winnipeg-Centre-Sud"),
            ("Winnipeg West", "Winnipeg-Ouest"),
        ],
        ["SK"] =
        [
            ("Battlefords\u2014Lloydminster\u2014Meadow Lake", "Battlefords\u2014Lloydminster\u2014Meadow Lake"),
            ("Carlton Trail\u2014Eagle Creek", "Sentier Carlton\u2014Eagle Creek"),
            ("Desnethe\u0301\u2014Missinippi\u2014Churchill River", "Desnethe\u0301\u2014Missinippi\u2014Rivi\u00e8re Churchill"),
            ("Moose Jaw\u2014Lake Centre\u2014Lanigan", "Moose Jaw\u2014Lake Centre\u2014Lanigan"),
            ("Prince Albert", "Prince Albert"),
            ("Regina\u2014Lewvan", "Regina\u2014Lewvan"),
            ("Regina\u2014Qu'Appelle", "Regina\u2014Qu'Appelle"),
            ("Regina\u2014Wascana", "Regina\u2014Wascana"),
            ("Saskatoon South", "Saskatoon-Sud"),
            ("Saskatoon\u2014University", "Saskatoon\u2014University"),
            ("Saskatoon West", "Saskatoon-Ouest"),
            ("Souris\u2014Moose Mountain", "Souris\u2014Moose Mountain"),
            ("Swift Current\u2014Grasslands\u2014Kindersley", "Swift Current\u2014Grasslands\u2014Kindersley"),
            ("Yorkton\u2014Melville", "Yorkton\u2014Melville"),
        ],
        ["AB"] =
        [
            ("Airdrie\u2014Cochrane", "Airdrie\u2014Cochrane"),
            ("Battle River\u2014Crowfoot", "Battle River\u2014Crowfoot"),
            ("Bow River", "Bow River"),
            ("Calgary Centre", "Calgary-Centre"),
            ("Calgary Confederation", "Calgary Confederation"),
            ("Calgary Crowfoot", "Calgary Crowfoot"),
            ("Calgary East", "Calgary-Est"),
            ("Calgary Heritage", "Calgary Heritage"),
            ("Calgary McKnight", "Calgary McKnight"),
            ("Calgary Midnapore", "Calgary Midnapore"),
            ("Calgary Nose Hill", "Calgary Nose Hill"),
            ("Calgary Shepard", "Calgary Shepard"),
            ("Calgary Signal Hill", "Calgary Signal Hill"),
            ("Calgary Skyview", "Calgary Skyview"),
            ("Edmonton Centre", "Edmonton-Centre"),
            ("Edmonton Gateway", "Edmonton Gateway"),
            ("Edmonton Griesbach", "Edmonton Griesbach"),
            ("Edmonton Manning", "Edmonton Manning"),
            ("Edmonton Northwest", "Edmonton-Nord-Ouest"),
            ("Edmonton Riverbend", "Edmonton Riverbend"),
            ("Edmonton Southeast", "Edmonton-Sud-Est"),
            ("Edmonton Strathcona", "Edmonton Strathcona"),
            ("Edmonton West", "Edmonton-Ouest"),
            ("Foothills", "Foothills"),
            ("Fort McMurray\u2014Cold Lake", "Fort McMurray\u2014Cold Lake"),
            ("Grande Prairie", "Grande Prairie"),
            ("Lakeland", "Lakeland"),
            ("Leduc\u2014Wetaskiwin", "Leduc\u2014Wetaskiwin"),
            ("Lethbridge", "Lethbridge"),
            ("Medicine Hat\u2014Cardston\u2014Warner", "Medicine Hat\u2014Cardston\u2014Warner"),
            ("Parkland", "Parkland"),
            ("Peace River\u2014Westlock", "Peace River\u2014Westlock"),
            ("Ponoka\u2014Didsbury", "Ponoka\u2014Didsbury"),
            ("Red Deer", "Red Deer"),
            ("Sherwood Park\u2014Fort Saskatchewan", "Sherwood Park\u2014Fort Saskatchewan"),
            ("St. Albert\u2014Sturgeon River", "St. Albert\u2014Sturgeon River"),
            ("Yellowhead", "Yellowhead"),
        ],
        ["BC"] =
        [
            ("Abbotsford\u2014South Langley", "Abbotsford\u2014Langley-Sud"),
            ("Burnaby Central", "Burnaby Central"),
            ("Burnaby North\u2014Seymour", "Burnaby-Nord\u2014Seymour"),
            ("Cariboo\u2014Prince George", "Cariboo\u2014Prince George"),
            ("Chilliwack\u2014Hope", "Chilliwack\u2014Hope"),
            ("Cloverdale\u2014Langley City", "Cloverdale\u2014Langley City"),
            ("Columbia\u2014Kootenay\u2014Southern Rockies", "Columbia\u2014Kootenay\u2014Southern Rockies"),
            ("Coquitlam\u2014Port Coquitlam", "Coquitlam\u2014Port Coquitlam"),
            ("Courtenay\u2014Alberni", "Courtenay\u2014Alberni"),
            ("Cowichan\u2014Malahat\u2014Langford", "Cowichan\u2014Malahat\u2014Langford"),
            ("Delta", "Delta"),
            ("Esquimalt\u2014Saanich\u2014Sooke", "Esquimalt\u2014Saanich\u2014Sooke"),
            ("Fleetwood\u2014Port Kells", "Fleetwood\u2014Port Kells"),
            ("Kamloops\u2014Shuswap\u2014Central Rockies", "Kamloops\u2014Shuswap\u2014Central Rockies"),
            ("Kamloops\u2014Thompson\u2014Nicola", "Kamloops\u2014Thompson\u2014Nicola"),
            ("Kelowna", "Kelowna"),
            ("Langley Township\u2014Fraser Heights", "Langley Township\u2014Fraser Heights"),
            ("Mission\u2014Matsqui\u2014Abbotsford", "Mission\u2014Matsqui\u2014Abbotsford"),
            ("Nanaimo\u2014Ladysmith", "Nanaimo\u2014Ladysmith"),
            ("New Westminster\u2014Burnaby\u2014Maillardville", "New Westminster\u2014Burnaby\u2014Maillardville"),
            ("North Island\u2014Powell River", "North Island\u2014Powell River"),
            ("North Vancouver\u2014Capilano", "North Vancouver\u2014Capilano"),
            ("Okanagan Lake West\u2014South Kelowna", "Okanagan Lake Ouest\u2014Kelowna-Sud"),
            ("Pitt Meadows\u2014Maple Ridge", "Pitt Meadows\u2014Maple Ridge"),
            ("Port Moody\u2014Coquitlam", "Port Moody\u2014Coquitlam"),
            ("Prince George\u2014Peace River\u2014Northern Rockies", "Prince George\u2014Peace River\u2014Northern Rockies"),
            ("Richmond Centre\u2014Marpole", "Richmond-Centre\u2014Marpole"),
            ("Richmond East\u2014Steveston", "Richmond-Est\u2014Steveston"),
            ("Saanich\u2014Gulf Islands", "Saanich\u2014Gulf Islands"),
            ("Similkameen\u2014South Okanagan\u2014West Kootenay", "Similkameen\u2014Okanagan-Sud\u2014Kootenay-Ouest"),
            ("Skeena\u2014Bulkley Valley", "Skeena\u2014Bulkley Valley"),
            ("South Surrey\u2014White Rock", "Surrey-Sud\u2014White Rock"),
            ("Surrey Centre", "Surrey-Centre"),
            ("Surrey Newton", "Surrey Newton"),
            ("Vancouver Centre", "Vancouver-Centre"),
            ("Vancouver East", "Vancouver-Est"),
            ("Vancouver Fraserview\u2014South Burnaby", "Vancouver Fraserview\u2014Burnaby-Sud"),
            ("Vancouver Granville", "Vancouver Granville"),
            ("Vancouver Kingsway", "Vancouver Kingsway"),
            ("Vancouver Quadra", "Vancouver Quadra"),
            ("Vernon\u2014Lake Country\u2014Monashee", "Vernon\u2014Lake Country\u2014Monashee"),
            ("Victoria", "Victoria"),
            ("West Vancouver\u2014Sunshine Coast\u2014Sea to Sky Country", "West Vancouver\u2014Sunshine Coast\u2014Sea to Sky Country"),
        ],
        ["YT"] =
        [
            ("Yukon", "Yukon"),
        ],
        ["NT"] =
        [
            ("Northwest Territories", "Territoires du Nord-Ouest"),
        ],
        ["NU"] =
        [
            ("Nunavut", "Nunavut"),
        ],
    };

    public static async Task RunAsync(string processedDir, string wwwrootDataDir)
    {
        Console.WriteLine("Generating sample data for development...");

        var rng = new Random(42);
        var ridings = new List<Riding>();
        var results2025 = new List<RidingResult>();
        var results2021 = new List<RidingResult>();
        int ridingId = 10001;

        foreach (var (province, abbr, region, numRidings) in Provinces)
        {
            var regionShares = Results2025[region];
            var names = RidingNames[abbr];

            for (int i = 0; i < numRidings; i++)
            {
                var (name, nameFr) = names[i];
                var (lat, lng) = HexLayoutGenerator.RidingCentroids.GetValueOrDefault(ridingId, (0, 0));
                ridings.Add(new Riding(ridingId, name, nameFr, province, region, lat, lng));

                // Generate 2025 results with local variation
                var candidates2025 = GenerateRidingResult(regionShares, rng, ridingId, 2025);
                results2025.Add(candidates2025);

                // Generate 2021 results (slightly different from 2025)
                var shifted = ShiftShares(regionShares, rng, 0.05);
                var candidates2021 = GenerateRidingResult(shifted, rng, ridingId, 2021);
                results2021.Add(candidates2021);

                ridingId++;
            }
        }

        // Generate current polling data (slightly different from 2025 results)
        var polling = new List<RegionalPoll>();
        foreach (var region in Enum.GetValues<Region>())
        {
            var shifted = ShiftShares(Results2025[region], rng, 0.03);
            polling.Add(new RegionalPoll(region, shifted));
        }

        // Generate riding-mps.json with placeholder names
        var ridingMps = new List<RidingMp>();
        foreach (var riding in ridings)
        {
            var winner2025 = results2025.First(r => r.RidingId == riding.Id)
                .Candidates.OrderByDescending(c => c.Votes).First();
            ridingMps.Add(new RidingMp(riding.Id, 2025, $"Sample MP {riding.Id}", winner2025.Party));

            var winner2021 = results2021.First(r => r.RidingId == riding.Id)
                .Candidates.OrderByDescending(c => c.Votes).First();
            ridingMps.Add(new RidingMp(riding.Id, 2021, $"Sample MP {riding.Id} (2021)", winner2021.Party));
        }

        // Write all files
        var files = new Dictionary<string, object>
        {
            ["ridings.json"] = ridings,
            ["results-2025.json"] = results2025,
            ["results-2021.json"] = results2021,
            ["polling.json"] = polling,
            ["riding-mps.json"] = ridingMps,
        };

        foreach (var (filename, data) in files)
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);

            var processedPath = Path.Combine(processedDir, filename);
            await File.WriteAllTextAsync(processedPath, json);

            var wwwrootPath = Path.Combine(wwwrootDataDir, filename);
            await File.WriteAllTextAsync(wwwrootPath, json);

            Console.WriteLine($"  Written {filename}");
        }

        Console.WriteLine($"Generated {ridings.Count} ridings with election results and polling data.");
    }

    private static RidingResult GenerateRidingResult(
        Dictionary<Party, double> regionShares, Random rng, int ridingId, int year)
    {
        int totalVotes = rng.Next(25000, 55000);
        var candidates = new List<CandidateResult>();
        double sumShares = 0;
        var shares = new Dictionary<Party, double>();

        foreach (var party in PartyColorProvider.MainParties)
        {
            double baseShare = regionShares.GetValueOrDefault(party, 0);
            // Add local variation (larger for smaller parties)
            double variation = (rng.NextDouble() - 0.5) * 0.08;
            double share = Math.Max(0, baseShare + variation);
            shares[party] = share;
            sumShares += share;
        }

        // Normalize and create candidates
        foreach (var (party, share) in shares)
        {
            double normalizedShare = sumShares > 0 ? share / sumShares : 0;
            if (normalizedShare < 0.005 && party != Party.BQ) continue; // Skip tiny parties
            if (party == Party.BQ && regionShares.GetValueOrDefault(Party.BQ, 0) < 0.01) continue;

            int votes = (int)(totalVotes * normalizedShare);
            candidates.Add(new CandidateResult(party, votes, normalizedShare));
        }

        int actualTotal = candidates.Sum(c => c.Votes);
        return new RidingResult(ridingId, year, candidates, actualTotal);
    }

    private static Dictionary<Party, double> ShiftShares(
        Dictionary<Party, double> baseShares, Random rng, double magnitude)
    {
        var shifted = new Dictionary<Party, double>();
        double sum = 0;
        foreach (var (party, share) in baseShares)
        {
            double newShare = Math.Max(0, share + (rng.NextDouble() - 0.5) * magnitude * 2);
            shifted[party] = newShare;
            sum += newShare;
        }
        // Normalize
        foreach (var party in shifted.Keys.ToList())
            shifted[party] /= sum;
        return shifted;
    }
}
