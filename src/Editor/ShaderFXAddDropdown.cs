using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXAddDropdown : AdvancedDropdown
    {
        private readonly Action<ShaderFXCatalog.Entry> select;
        private readonly Action createCustom;

        private sealed class ActionItem : AdvancedDropdownItem
        {
            internal readonly Action action;
            internal ActionItem(string name, Action action) : base(name) => this.action = action;
        }

        private sealed class PresetItem : AdvancedDropdownItem
        {
            internal readonly ShaderFXCatalog.Entry entry;
            internal PresetItem(string name, ShaderFXCatalog.Entry entry) : base(name)
            {
                this.entry = entry;
                enabled = !entry.HasError;
                ShaderFXIconCache.Bind(this, entry.icon, entry.path);
            }
        }

        private sealed class MenuNode
        {
            internal readonly Dictionary<string, MenuNode> children = new Dictionary<string, MenuNode>(StringComparer.Ordinal);
            internal readonly List<ShaderFXCatalog.Entry> presets = new List<ShaderFXCatalog.Entry>();
        }

        internal ShaderFXAddDropdown(Action<ShaderFXCatalog.Entry> select, Action createCustom)
            : base(new AdvancedDropdownState())
        {
            this.select = select;
            this.createCustom = createCustom;
            minimumSize = new Vector2(340, 400);
        }

        internal static Button CreateButton(Action<ShaderFXCatalog.Entry> select, Action createCustom)
        {
            var button = new Button { name = "addFx", text = "Add FX", tooltip = "Search effect presets or create a custom Shader FX." };
            button.clicked += () => new ShaderFXAddDropdown(select, createCustom).Show(button.worldBound);
            return button;
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Add FX");
            root.AddChild(new ActionItem("Custom Shader FX", createCustom) { icon = ShaderFXIconCache.Default });
            root.AddSeparator();
            AddPresets(root, ShaderFXCatalog.GetMenuEntries());
            root.AddSeparator();
            root.AddChild(new ActionItem("Open User Shader FX Folder", ShaderFXCatalog.OpenUserFolder));
            root.AddChild(new ActionItem("Library Settings…", WhimTexUserSettingsWindow.Open));
            return root;
        }

        private static void AddPresets(AdvancedDropdownItem root, IReadOnlyList<ShaderFXCatalog.Entry> list)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var tree = new MenuNode();
            foreach (var entry in list)
            {
                string path = (entry.user ? "User/" : "") + entry.menuPath;
                counts.TryGetValue(path, out int count);
                counts[path] = count + 1;
                string[] parts = path.Split('/');
                var parent = tree;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!parent.children.TryGetValue(parts[i], out var child))
                    {
                        child = new MenuNode();
                        parent.children.Add(parts[i], child);
                    }
                    parent = child;
                }
                parent.presets.Add(entry);
            }
            if (list.Count == 0)
                root.AddChild(new AdvancedDropdownItem(ShaderFXCatalog.EmptyMenuMessage) { enabled = false });
            Populate(root, tree, counts);
        }

        private static void Populate(AdvancedDropdownItem parent, MenuNode node, Dictionary<string, int> counts)
        {
            // Submenus and ungrouped presets share the first block. Groups never create navigation levels.
            var blocks = new SortedDictionary<string, List<(string name, string path, AdvancedDropdownItem item)>>(StringComparer.OrdinalIgnoreCase);
            var ungrouped = new List<(string name, string path, AdvancedDropdownItem item)>();
            blocks.Add("", ungrouped);
            foreach (var child in node.children)
            {
                var item = new AdvancedDropdownItem(child.Key);
                ShaderFXIconCache.BindCategory(item);
                Populate(item, child.Value, counts);
                ungrouped.Add((child.Key, child.Key, item));
            }
            foreach (var entry in node.presets)
            {
                string path = (entry.user ? "User/" : "") + entry.menuPath;
                string name = entry.menuPath.Substring(entry.menuPath.LastIndexOf('/') + 1);
                string label = name;
                if (counts[path] > 1) label += " (" + entry.path.Replace('\\', '›').Replace('/', '›') + ")";
                if (entry.HasError) label += " — " + entry.error;
                string group = entry.group ?? "";
                if (!blocks.TryGetValue(group, out var block))
                    blocks.Add(group, block = new List<(string name, string path, AdvancedDropdownItem item)>());
                block.Add((name, entry.path, new PresetItem(label, entry)));
            }
            bool populated = false;
            foreach (var block in blocks.Values)
            {
                if (block.Count == 0) continue;
                block.Sort((a, b) =>
                {
                    int comparison = string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
                    if (comparison == 0) comparison = string.Compare(a.name, b.name, StringComparison.Ordinal);
                    return comparison != 0 ? comparison : string.Compare(a.path, b.path, StringComparison.Ordinal);
                });
                if (populated) parent.AddSeparator();
                foreach (var row in block) parent.AddChild(row.item);
                populated = true;
            }
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item == null || !item.enabled) return;
            if (item is PresetItem preset) select(preset.entry);
            else if (item is ActionItem command) command.action();
        }
    }
}
