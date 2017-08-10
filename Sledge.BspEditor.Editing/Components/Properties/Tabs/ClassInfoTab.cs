﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Components.Properties.SmartEdit;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Logging;
using Sledge.Common.Shell.Context;
using Sledge.Common.Translations;
using Sledge.DataStructures.GameData;
using Sledge.Shell;

namespace Sledge.BspEditor.Editing.Components.Properties.Tabs
{
    [AutoTranslate]
    [Export(typeof(IObjectPropertyEditorTab))]
    public partial class ClassInfoTab : UserControl, IObjectPropertyEditorTab
    {
        [ImportMany] private IEnumerable<Lazy<SmartEditControl>> _smartEditControls;
        [Import("Default")] private SmartEditControl _defaultControl;
        private List<TableValue> _tableValues;
        private WeakReference<MapDocument> _document;

        public string OrderHint => "D";
        public Control Control => this;

        #region Translations

        public string ClassLabel
        {
            get => lblClass.Text;
            set => this.Invoke(() => lblClass.Text = value);
        }

        public string KeyValuesLabel
        {
            get => lblKeyValues.Text;
            set => this.Invoke(() => lblKeyValues.Text = value);
        }

        public string PropertyNameLabel
        {
            get => colPropertyName.Text;
            set => this.Invoke(() => colPropertyName.Text = value);
        }

        public string PropertyValueLabel
        {
            get => colPropertyValue.Text;
            set => this.Invoke(() => colPropertyValue.Text = value);
        }

        public string HelpLabel
        {
            get => lblHelp.Text;
            set => this.Invoke(() => lblHelp.Text = value);
        }

        public string CommentsLabel
        {
            get => lblComments.Text;
            set => this.Invoke(() => lblComments.Text = value);
        }

        public string SmartEditButton
        {
            get => btnSmartEdit.Text;
            set => this.Invoke(() => btnSmartEdit.Text = value);
        }

        public string HelpButton
        {
            get => btnHelp.Text;
            set => this.Invoke(() => btnHelp.Text = value);
        }

        public string CopyButton
        {
            get => btnCopy.Text;
            set => this.Invoke(() => btnCopy.Text = value);
        }

        public string PasteButton
        {
            get => btnPaste.Text;
            set => this.Invoke(() => btnPaste.Text = value);
        }

        public string AddButton
        {
            get => btnAdd.Text;
            set => this.Invoke(() => btnAdd.Text = value);
        }

        public string DeleteButton
        {
            get => btnDelete.Text;
            set => this.Invoke(() => btnDelete.Text = value);
        }

        public string MultipleClassesText { get; set; }
        public string MultipleValuesText { get; set; }

        #endregion

        public bool HasChanges
        {
            get { return false; }
        }

        public ClassInfoTab()
        {
            InitializeComponent();
            CreateHandle();

            _tableValues = new List<TableValue>();
            _document = new WeakReference<MapDocument>(null);
        }

        public bool IsInContext(IContext context)
        {
            return context.TryGet("ActiveDocument", out MapDocument doc) &&
                   doc.Selection.GetSelectedParents().Any(x => x.Data.GetOne<EntityData>() != null);
        }

        public async Task SetObjects(MapDocument document, List<IMapObject> objects)
        {
            _document = new WeakReference<MapDocument>(document);
            GameData gd = null;
            if (document != null) gd = await document.Environment.GetGameData();
            this.Invoke(() =>
            {
                UpdateObjects(gd ?? new GameData(), document, objects);
            });
        }

        public IEnumerable<IOperation> GetChanges(MapDocument document, List<IMapObject> objects)
        {
            yield break;
        }

        private void UpdateObjects(GameData gameData, MapDocument document, List<IMapObject> objects)
        {
            SuspendLayout();

            var datas = objects
                .Select(x => new {Object = x, EntityData = x.Data.GetOne<EntityData>()})
                .Where(x => x.EntityData != null)
                .ToList();

            // Update the class list
            cmbClass.BeginUpdate();

            cmbClass.Items.Clear();
            var gameDataClasses = gameData.Classes
                .Where(x => x.ClassType != ClassType.Base)
                .OrderBy(x => x.Name, StringComparer.InvariantCultureIgnoreCase)
                .Select(x => x.Name)
                .OfType<object>()
                .ToArray();
            cmbClass.Items.AddRange(gameDataClasses);

            var classes = datas
                .Select(x => x.EntityData.Name)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            GameDataObject gdo = null;

            if (classes.Count == 0)
            {
                cmbClass.Text = "";
            }
            else if (classes.Count > 1)
            {
                cmbClass.Text = MultipleClassesText + @" " + String.Join("; ", classes);
            }
            else if (classes.Count == 1)
            {
                gdo = gameData.Classes.FirstOrDefault(x => x.ClassType != ClassType.Base && String.Equals(x.Name, classes[0], StringComparison.InvariantCultureIgnoreCase));
                cmbClass.Text = classes[0];
            }

            cmbClass.EndUpdate();

            // Update the keyvalues
            _tableValues = TableValue.Create(gdo, gdo?.Name, datas.Select(x => x.EntityData).ToList(), MultipleValuesText);
            UpdateTable();
            
            // Clear smartedit panel
            pnlSmartEdit.Controls.Clear();

            ResumeLayout();
        }

        private void UpdateTable()
        {
            var smartEdit = btnSmartEdit.Checked;

            lstKeyValues.BeginUpdate();

            lstKeyValues.Items.Clear();

            angAngles.Enabled = false;
            angAngles.SetAngle(0);

            foreach (var tv in _tableValues)
            {
                var keyText = tv.NewKey;
                var valText = tv.Value;

                if (smartEdit)
                {
                    keyText = tv.DisplayText;
                    valText = tv.DisplayValue;
                }

                lstKeyValues.Items.Add(new ListViewItem(keyText)
                {
                    Tag = tv,
                    BackColor = tv.Colour
                }).SubItems.Add(valText);

                if (tv.NewKey == "angles")
                {
                    angAngles.Enabled = true;
                    angAngles.SetAnglePropertyString(tv.Value);
                }
            }

            lstKeyValues.EndUpdate();
        }

        private void SelectedPropertyChanged(object sender, EventArgs e)
        {
            var sel = lstKeyValues.SelectedItems.OfType<ListViewItem>().FirstOrDefault();
            var tv = sel?.Tag as TableValue;

            pnlSmartEdit.Controls.Clear();
            if (tv != null)
            {
                var prop = btnSmartEdit.Checked ? tv.GameDataObject?.Properties.FirstOrDefault(x => x.Name == tv.NewKey) : null;
                var type = prop?.VariableType ?? VariableType.Void;
                var edit = _smartEditControls
                               .Select(x => x.Value)
                               .OrderBy(x => x.PriorityHint)
                               .FirstOrDefault(x => x.SupportsType(type)) ?? _defaultControl;

                _document.TryGetTarget(out MapDocument doc);
                edit.SetProperty(doc, tv.OriginalKey, tv.NewKey, tv.Value, prop);
                pnlSmartEdit.Controls.Add(edit.Control);
            }
        }

        private void SmartEditToggled(object sender, EventArgs e) => UpdateTable();
    }
}