using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Characters;
using Cthangover.Core.Items;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Utils;
using Godot;

namespace Mods.TestDefaultsAndPatches
{
    /// <summary>
    /// Entry point of the property patches itest: active only with
    /// <c>--itest=property_patches</c>. The fixture itself is loaded only by
    /// <c>--with-mods=test_defaults_and_patches</c> (manifest <c>fixture</c>).
    /// </summary>
    public class PropertyPatchesTestInitializer : IModInitializer
    {
        private static bool queued;

        public void OnModLoaded(string modId) { }

        public void OnModResourcesReady()
        {
            if (queued || !OS.GetCmdlineUserArgs().Contains("--itest=property_patches"))
                return;
            queued = true;
            GameLogger.Log("TEST", "PropertyPatchesTest: activating");
            ModRoot.AddDeferred(new PropertyPatchesTestDriverNode());
        }
    }

    /// <summary>
    /// Checks the merge of <c>patches/character.json</c> and
    /// <c>patches/item.json</c> into the real entities of other mods, as the
    /// game resolves them: a targeted patch wins over the wildcard, the
    /// wildcard reaches every entity of the kind, and untouched typed fields
    /// survive. Prints <c>[TEST_RESULT] name=property_patches</c> and quits.
    /// </summary>
    public partial class PropertyPatchesTestDriverNode : Node
    {
        private const string ResultName = "property_patches";
        private const double TimeoutSeconds = 60;

        private int passed;
        private int failed;
        private double elapsed;
        private bool done;

        public override void _Process(double delta)
        {
            if (done)
                return;
            elapsed += delta;
            if (elapsed > TimeoutSeconds)
            {
                Check("boot finished in time", false);
                Finish();
                return;
            }

            if (!Boot.IsReady)
                return;

            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Check($"no exception ({ex.GetType().Name}: {ex.Message})", false);
            }

            Finish();
        }

        private void Run()
        {
            var characterPatches = Patches.Collect("character");
            Check("character patches collected (Marao + *)", characterPatches != null && characterPatches.ContainsKey("Marao") && characterPatches.ContainsKey("*"));
            var itemPatches = Patches.Collect("item");
            Check("item patches collected (food/ration + *)", itemPatches != null && itemPatches.ContainsKey("food/ration") && itemPatches.ContainsKey("*"));

            var characters = Entities.Collect<CharacterInfo>();
            Check("Marao resolved", characters.ContainsKey("Marao"));
            if (characters.TryGetValue("Marao", out var marao))
            {
                Expect(marao.ExtraProperties, "TestPatched", "42", "Marao: targeted value");
                Expect(marao.ExtraProperties, "TestDefault", "100", "Marao: targeted beats wildcard");
                Expect(marao.ExtraProperties, "TestGlobalPatch", "1", "Marao: wildcard-only key");
            }

            var other = characters.Values.FirstOrDefault(c => c.ID != "Marao");
            Check("another character exists", other != null);
            if (other != null)
            {
                Expect(other.ExtraProperties, "TestDefault", "-20", $"{other.ID}: wildcard value");
                Check($"{other.ID}: no targeted key", other.ExtraProperties == null || !other.ExtraProperties.ContainsKey("TestPatched"));
            }

            var items = Entities.Collect<ItemInfo>();
            if (items.TryGetValue("food/ration", out var ration))
            {
                Expect(ration.ExtraProperties, "Durability", "99", "ration: targeted beats wildcard");
                Expect(ration.ExtraProperties, "SpecialProp", "true", "ration: targeted-only key");
                Expect(ration.ExtraProperties, "GlobalItemProp", "patched", "ration: wildcard-only key");
                Check("ration: typed field survives the merge", !string.IsNullOrEmpty(ration.Name));
            }
            else
                Check("food/ration resolved", false);

            if (items.TryGetValue("food/wolf_meat", out var meat))
                Expect(meat.ExtraProperties, "Durability", "50", "wolf_meat: wildcard value");
            else
                Check("food/wolf_meat resolved", false);
        }

        private void Expect(Dictionary<string, object> properties, string key, string expected, string what)
        {
            var actual = properties != null && properties.TryGetValue(key, out var value) ? Text(value) : null;
            Check($"{what}: {key}={expected}" + (actual == expected ? string.Empty : $" (got {actual ?? "nothing"})"), actual == expected);
        }

        private static string Text(object value) => value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } s => s.GetString(),
            JsonElement e => e.GetRawText(),
            bool b => b ? "true" : "false",
            null => null,
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };

        private void Check(string what, bool ok)
        {
            if (ok)
                passed++;
            else
                failed++;
            GameLogger.Log("TEST", $"  [{(ok ? "PASS" : "FAIL")}] {what}");
        }

        private void Finish()
        {
            done = true;
            GameLogger.Log("TEST", $"[TEST_RESULT] name={ResultName} passed={passed} failed={failed}");
            GetTree().Quit(failed == 0 ? 0 : 1);
        }
    }
}
