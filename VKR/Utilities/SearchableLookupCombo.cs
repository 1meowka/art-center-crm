#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VKR.Models;

namespace VKR.Utilities;

public sealed class SearchableLookupCombo
{
    private readonly ComboBox _combo;
    private List<LookupItem> _all = new();
    private bool _syncing;

    public SearchableLookupCombo(ComboBox combo)
    {
        _combo = combo;
        _combo.IsEditable = true;
        _combo.IsTextSearchEnabled = false;
        _combo.StaysOpenOnEdit = true;
        _combo.DisplayMemberPath = "Name";
        _combo.SelectedValuePath = "Id";
        _combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged));
        _combo.SelectionChanged += (_, _) => SyncTextFromSelection();
        _combo.DropDownClosed += (_, _) => SyncTextFromSelection();
        _combo.GotKeyboardFocus += (_, _) => TryOpenDropDown(showAllIfExactMatch: true);
        _combo.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        _combo.DropDownOpened += (_, _) => ApplyFilter(_combo.Text, open: false, showAllIfExactMatch: true);
    }

    public void SetItems(IEnumerable<LookupItem> items)
    {
        _syncing = true;
        _all = items
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        LookupComboHelper.SetItems(_combo, _all);
        _combo.SelectedItem = null;
        _combo.Text = string.Empty;
        _syncing = false;
    }

    public void SelectById(int? id)
    {
        LookupItem? match = id.HasValue
            ? _all.FirstOrDefault(item => item.Id == id.Value)
            : _all.FirstOrDefault(item => item.Id == 0);

        _syncing = true;
        _combo.ItemsSource = _all;
        _combo.DisplayMemberPath = "Name";
        _combo.SelectedValuePath = "Id";
        if (match != null)
        {
            _combo.SelectedItem = match;
            _combo.SelectedValue = match.Id;
            _combo.Text = match.Name;
        }
        else
        {
            _combo.SelectedItem = null;
            _combo.SelectedValue = null;
            _combo.Text = string.Empty;
        }
        _syncing = false;
    }

    public void SetText(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        var match = _all.FirstOrDefault(item =>
            string.Equals(item.Name, text, StringComparison.CurrentCultureIgnoreCase));

        if (match != null)
        {
            SelectById(match.Id);
            return;
        }

        _syncing = true;
        _combo.SelectedItem = null;
        _combo.Text = text;
        _syncing = false;
    }

    public string GetText()
    {
        if (_combo.SelectedItem is LookupItem item && !string.IsNullOrWhiteSpace(item.Name))
            return item.Name.Trim();
        return (_combo.Text ?? string.Empty).Trim();
    }

    public int? GetSelectedId()
    {
        if (_combo.SelectedItem is LookupItem selected && itemId(selected) is int fromSelection)
            return fromSelection;

        var text = GetText();
        var match = _all.FirstOrDefault(item =>
            string.Equals(item.Name, text, StringComparison.CurrentCultureIgnoreCase));
        return itemId(match);

        static int? itemId(LookupItem? item)
            => item is { Id: > 0 } ? item.Id : null;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_syncing || _all.Count == 0)
            return;

        if (!_combo.IsKeyboardFocusWithin)
            return;

        TryOpenDropDown(showAllIfExactMatch: true);
    }

    private void TryOpenDropDown(bool showAllIfExactMatch)
    {
        if (_syncing || _all.Count == 0)
            return;

        ApplyFilter(_combo.Text, open: true, showAllIfExactMatch);
    }

    private void SyncTextFromSelection()
    {
        if (_syncing || _combo.SelectedItem is not LookupItem selected)
            return;
        if (string.Equals(selected.Name, _combo.Text, StringComparison.CurrentCultureIgnoreCase))
            return;
        _syncing = true;
        _combo.Text = selected.Name;
        _syncing = false;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _all.Count == 0)
            return;

        if (_combo.SelectedItem is LookupItem selected)
        {
            var text = _combo.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                _syncing = true;
                _combo.Text = selected.Name;
                _syncing = false;
                return;
            }
            if (string.Equals(selected.Name, text, StringComparison.CurrentCultureIgnoreCase))
                return;
        }

        ApplyFilter(_combo.Text, open: true, showAllIfExactMatch: false);
    }

    private void ApplyFilter(string? rawText, bool open, bool showAllIfExactMatch)
    {
        var query = (rawText ?? string.Empty).Trim();
        var exactMatch = _combo.SelectedItem is LookupItem selected
            && string.Equals(selected.Name, query, StringComparison.CurrentCultureIgnoreCase);

        var filtered = string.IsNullOrEmpty(query) || (showAllIfExactMatch && exactMatch)
            ? _all
            : _all.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var typed = _combo.Text;
        _syncing = true;
        _combo.ItemsSource = filtered;
        _combo.DisplayMemberPath = "Name";
        _combo.SelectedValuePath = "Id";
        if (exactMatch && _combo.SelectedItem is LookupItem keep)
        {
            var stillThere = filtered.FirstOrDefault(item => item.Id == keep.Id);
            if (stillThere != null)
                _combo.SelectedItem = stillThere;
        }
        _combo.Text = typed;
        _syncing = false;

        if (open && filtered.Count > 0)
            _combo.IsDropDownOpen = true;
    }
}
