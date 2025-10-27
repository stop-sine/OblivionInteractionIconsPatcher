using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Noggog;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;

namespace OblivionInteractionIconsPatcher
{
    partial class Progam
    {
        private static readonly ModKey[] BethesdaPlugins =
        [
            Skyrim.ModKey, Update.ModKey, Dawnguard.ModKey,
            Dragonborn.ModKey, HearthFires.ModKey
        ];

        private static readonly HashSet<ModKey> CreationClubPlugins
        = GetCreationClubPlugins(GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE).CreationClubListingsFilePath ?? string.Empty);

        private static HashSet<ModKey> GetCreationClubPlugins(FilePath CreationClubListingsFilePath)
        {
            try
            {
                if (!File.Exists(CreationClubListingsFilePath))
                    return [];
                return [.. File.ReadAllLines(CreationClubListingsFilePath)
                .Select(line => ModKey.TryFromFileName(new FileName(line)))
                .Where(plugin => plugin.HasValue)
                .Select(plugin => plugin!.Value)];
            }
            catch
            {
                return [];
            }
        }

        private static bool PluginFilter(ISkyrimModGetter? mod)
        {
            if (mod is null) return false;
            if (BethesdaPlugins.Contains(mod.ModKey)) return false;
            if (CreationClubPlugins.Contains(mod.ModKey)) return false;
            if (mod.ModKey.Name.Contains("skymoji")) return false;
            if (mod.Florae.Count == 0 && mod.Activators.Count == 0) return false;
            return true;
        }

        private static string PackageFormKey(FormKey formKey)
        {
            return formKey.IDString() + "|" + formKey.ModKey.FileName;
        }

        private static string PackageIcon(string iconCharacter, string? iconColor)
        {
            var icon = "<font face='$Iconographia'> " + iconCharacter + " </font>";
            if (!iconColor.IsNullOrEmpty())
                icon = "<font color='" + iconColor + "'>" + icon + "</font>";
            return icon;
        }

        public static void Main(string[] args)
        {
            //Set Skyrim SE environment
            var env = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE);
            //Path to Skyrim SE data directory
            var dataPath = env.DataFolderPath.Path;
            //Path to Dynamic String Distributor directory
            var dsdPath = Directory.CreateDirectory(Path.Combine(dataPath, "SKSE\\Plugins\\DynamicStringDistributor1"));
            Directory.SetCurrentDirectory(dsdPath.FullName);

            string? iconColor = null;
            List<Record>? data = null;
            if (File.Exists("skyrim.esm\\skymojiactivators10.json"))
            {
                try
                {
                    data = JsonSerializer.Deserialize<List<Record>>(File.ReadAllText("skyrim.esm\\skymojiactivators10.json"));
                    if (data == null || data.Count == 0)
                    {
                        Console.WriteLine("Error: Deserialized data is null or empty.");
                        return;
                    }
                    var match = MyRegex().Match(data.First().@string);
                    if (match.Success)
                        iconColor = match.Groups[1].Value;
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Error deserializing JSON: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                }
            }

            var plugins = env.LoadOrder.ListedOrder.OnlyEnabled().Select(m => m.Mod).Where(PluginFilter).ToList();

            var serializeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            //Iterate through all plugins
            foreach (var plugin in plugins)
            {
                //Skip empty plugins
                if (plugin is null) continue;


                //Create collection of flora records
                var florae = new List<Record>();
                //Iterate through flora records in plugin
                foreach (var flora in plugin.Florae)
                {
                    if (flora.FormKey.ModKey != plugin.ModKey)
                    {
                        var origin = flora.FormKey.ToLink<IFloraGetter>().ResolveAll(env.LinkCache).Last();
                        if (flora.ActivateTextOverride == origin.ActivateTextOverride && flora.Name == origin.Name) continue;
                    }

                    //Default icon
                    string iconCharacter = "Q";

                    var full = flora.Name?.String;
                    var rnam = flora.ActivateTextOverride?.String;

                    //Mushrooms
                    if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMIngredientMushroomUp.FormKey)
                    || full.Contains(["spore", "cap", "crown", "shroom"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "A";
                    //Clams
                    else if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMIngredientClamUp.FormKey)
                    || full.ContainsNullable("clam", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "b";
                    //Fill | Cask or Barrel (Fill)
                    else if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMPotionUpSD.FormKey)
                    || rnam.ContainsNullable("fill bottles", StringComparison.OrdinalIgnoreCase)
                    || full.Contains(["barrel", "cask"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "L";
                    //Coin Pouch
                    else if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMCoinPouchUp.FormKey)
                    || flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMCoinPouchDown.FormKey)
                    || full.ContainsNullable("coin purse", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "S";
                    //Catch, Scavenge
                    else if (rnam.Equals(["catch", "scavenge"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "S";
                    //Other Flora
                    else
                        iconCharacter = "Q";

                    //Create entry for record
                    var record = new Record
                    {
                        form_id = PackageFormKey(flora.FormKey),
                        type = "FLOR RNAM",
                        @string = PackageIcon(iconCharacter, iconColor)

                    };
                    florae.Add(record);
                }

                //Create collection of activator records
                var activators = new List<Record>();
                //Iterate through activator records in plugin
                foreach (var activator in plugin.Activators)
                {
                    if (activator.FormKey.ModKey != plugin.ModKey)
                    {
                        var origin = activator.FormKey.ToLink<IActivatorGetter>().ResolveAll(env.LinkCache).Last();
                        if (activator.ActivateTextOverride?.String == origin.ActivateTextOverride?.String && activator.Name?.String == origin.Name?.String) continue;
                    }

                    //Default
                    string iconCharacter = "W";

                    var full = activator.Name?.String;
                    var edid = activator.EditorID;
                    var rnam = activator.ActivateTextOverride?.String;

                    //Exclude superfluous entries
                    if (activator.ActivateTextOverride == null && edid.Contains(["trigger", "fx"], StringComparison.OrdinalIgnoreCase))
                        continue;
                    //Steal
                    else if (rnam.EqualsNullable("steal", StringComparison.OrdinalIgnoreCase))
                    {
                        iconColor = "#ff0000";
                        iconCharacter = "S";
                    }
                    //Pickpocket
                    else if (rnam.EqualsNullable("pickpocket", StringComparison.OrdinalIgnoreCase))
                    {
                        iconColor = "#ff0000";
                        iconCharacter = "b";
                    }
                    //Steal From
                    else if (rnam.EqualsNullable("steal from", StringComparison.OrdinalIgnoreCase))
                    {
                        iconColor = "#ff0000";
                        iconCharacter = "V";
                    }
                    //Close
                    else if (rnam.EqualsNullable("close", StringComparison.OrdinalIgnoreCase))
                    {
                        iconColor = "#dddddd";
                        iconCharacter = "X";
                    }
                    //Chest | Search | Open Chest
                    else if (full.EqualsNullable("chest", StringComparison.OrdinalIgnoreCase)
                            || rnam.EqualsNullable("search", StringComparison.OrdinalIgnoreCase)
                            || full.ContainsNullable("chest", StringComparison.OrdinalIgnoreCase) && rnam.EqualsNullable("open", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "V";
                    //Grab & Touch
                    else if (rnam.Equals(["grab", "touch"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "S";
                    //Levers
                    else if (activator.Keywords != null && activator.Keywords.Contains(Skyrim.Keyword.ActivatorLever.FormKey)
                            || full.ContainsNullable("lever", StringComparison.OrdinalIgnoreCase)
                            || edid.ContainsNullable("pullbar", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "D";
                    //Chains
                    else if (full.ContainsNullable("chain", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "E";
                    //Mine
                    else if (rnam.EqualsNullable("mine", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "G";
                    //Button | Press, Examine, Push, Investigate
                    else if (full.ContainsNullable("button", StringComparison.OrdinalIgnoreCase)
                            || rnam.Equals(["press", "examine", "push", "investigate"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "F";
                    //Business Ledger | Write
                    else if (full.ContainsNullable("ledger", StringComparison.OrdinalIgnoreCase)
                            || rnam.EqualsNullable("write", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "H";
                    //Pray
                    else if (full.Contains(["shrine", "altar"], StringComparison.OrdinalIgnoreCase)
                            || edid.ContainsNullable("dlc2standingstone", StringComparison.OrdinalIgnoreCase)
                            || rnam.Equals(["pray", "worship"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "C";
                    //Drink
                    else if (rnam.EqualsNullable("drink", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "J";
                    //Eat
                    else if (rnam.EqualsNullable("eat", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "K";
                    //Drop, Place, Exchange
                    else if (rnam.Equals(["drop", "place", "exchange"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "N";
                    //Pick up
                    else if (rnam.EqualsNullable("pick up", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "O";
                    //Read
                    else if (rnam.EqualsNullable("read", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "P";
                    //Harvest
                    else if (rnam.EqualsNullable("harvest", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "Q";
                    //Take or Catch
                    else if (rnam.Equals(["take", "catch"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "S";
                    //Talk, Speak
                    else if (rnam.Equals(["talk", "speak"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "T";
                    //Sit
                    else if (rnam.EqualsNullable("sit", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "U";
                    //Open (Door)
                    else if (rnam.EqualsNullable("open", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "X";
                    //Activate
                    else if (rnam.EqualsNullable("activate", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "Y";
                    //Unlock
                    else if (rnam.EqualsNullable("unlock", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "Z";
                    //Sleep
                    else if (rnam.EqualsNullable("sleep", StringComparison.OrdinalIgnoreCase)
                            || full.Contains(["bed", "hammock", "coffin"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "a";
                    //Torch
                    else if (edid.ContainsNullable("sconce", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "i";
                    //Dragon Claw
                    else if (full.ContainsNullable("keyhole", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "j";
                    //Civil War Map & Map Marker (Flags)
                    else if (edid.ContainsNullable("cwmap", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "F";
                    //EVG Ladder | Float, Climb
                    else if (edid.ContainsNullable("ladder", StringComparison.OrdinalIgnoreCase)
                            || rnam.Equals(["float", "climb"], StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "d";
                    //EVG Squeeze
                    else if (edid.ContainsNullable("squeeze", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "e";
                    //CC Fishing
                    else if (full.ContainsNullable("fishing supplies", StringComparison.OrdinalIgnoreCase))
                        iconCharacter = "I";
                    else
                        iconCharacter = "W";

                    //Create entry for record
                    var record = new Record
                    {
                        form_id = PackageFormKey(activator.FormKey),
                        type = "ACTI RNAM",
                        @string = PackageIcon(iconCharacter, iconColor)

                    };
                    activators.Add(record);
                }

                if (florae.Count > 0 || activators.Count > 0)
                {
                    Console.WriteLine(plugin.ModKey.FileName);
                    var jsonDirectory = Directory.CreateDirectory(plugin.ModKey.FileName);
                    Directory.SetCurrentDirectory(jsonDirectory.Name);
                    var jsonPath = plugin.ModKey.Name.ToLower();
                    var floraJsonStrings = JsonSerializer.Serialize(florae, serializeOptions);
                    if (florae.Count > 0)
                        File.WriteAllText(jsonPath + "flora.json", floraJsonStrings);
                    var activatorJsonStrings = JsonSerializer.Serialize(activators, serializeOptions);
                    if (activators.Count > 0)
                        File.WriteAllText(jsonPath + "acti.json", activatorJsonStrings);
                    Directory.SetCurrentDirectory(dsdPath.FullName);
                }

            }
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"color\s*=\s*['""]#([0-9A-Fa-f]{6})['""]")]
        private static partial System.Text.RegularExpressions.Regex MyRegex();
    }
}
