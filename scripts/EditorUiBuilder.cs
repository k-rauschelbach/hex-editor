namespace HexEditor.scripts;

using Godot;
using System;

public static class EditorUiBuilder
{
    public struct UiRefs
    {
        public SpinBox HeightSpinBox;
        public Label ChunkCountLabel;
    }

    public static UiRefs Build(
        Control uiRoot,
        BrushTool brushTool,
        Action<EditorMain.EditMode> onModeChanged,
        Action onDeselectAll,
        Action<double> onHeightChanged,
        Action<bool> onSlopeConstraintToggled,
        Action<bool> onOverlayToggled,
        Action<float> onMaxDeviationChanged,
        Action<float> onMaxStepHeightChanged,
        Action<string> onSave,
        Action<string> onLoad)
    {
        // Make the parent control span the full viewport so child anchors
        // work relative to screen size (not the tiny 40x40 default from the scene).
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        uiRoot.SetOffsetsPreset(Control.LayoutPreset.FullRect);
        // Ignore mouse on this full-screen control so clicks pass through to the 3D viewport
        uiRoot.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Create panel on the right side
        var panel = new PanelContainer();
        // position panel on right edge of frame
        panel.AnchorLeft = 1.0f;
        panel.AnchorRight = 1.0f;
        panel.AnchorTop = 0.0f;
        panel.AnchorBottom = 1.0f;
        panel.OffsetLeft = -220;
        panel.OffsetRight = 0;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);

        // Title
        var title = new Label();
        title.Text = "Terrain Editor";
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        // Edit mode selector
        var modeLabel = new Label();
        modeLabel.Text = "Edit Mode:";
        vbox.AddChild(modeLabel);

        var modeDropdown = new OptionButton();
        modeDropdown.AddItem("Single Vertex", 0);
        modeDropdown.AddItem("Brush", 1);
        modeDropdown.Selected = 0;
        modeDropdown.ItemSelected += (long index) =>
        {
            onModeChanged((EditorMain.EditMode)index);
        };
        vbox.AddChild(modeDropdown);

        // Brush Controls
        var brushUI = brushTool.CreateUI();
        vbox.AddChild(brushUI);
        vbox.AddChild(new HSeparator());

        // --- Walkability --- //
        Label walkHeader = new Label();
        walkHeader.Text = "---Walkability---";
        vbox.AddChild(walkHeader);

        // Overlay toggle
        CheckBox overlayToggle = new CheckBox();
        overlayToggle.Text = "Show Overlay";
        overlayToggle.ButtonPressed = true;
        overlayToggle.Toggled += (bool on) => onOverlayToggled(on);
        vbox.AddChild(overlayToggle);

        // Constraint toggle
        CheckBox constraintToggle = new CheckBox();
        constraintToggle.Text = "Slope Constraint";
        constraintToggle.ButtonPressed = true;
        constraintToggle.Toggled += (bool on) => onSlopeConstraintToggled(on);
        vbox.AddChild(constraintToggle);

        // Max Deviation control
        Label devLabel = new Label();
        devLabel.Text = "Max Deviation:";
        vbox.AddChild(devLabel);

        SpinBox devSpinBox = new SpinBox();
        devSpinBox.MinValue = 0.1;
        devSpinBox.MaxValue = 5.0;
        devSpinBox.Step = 0.1;
        devSpinBox.Value = 0.5;
        devSpinBox.ValueChanged += (double val) => onMaxDeviationChanged((float)val);
        vbox.AddChild(devSpinBox);

        // Max step height control
        Label stepLabel = new Label();
        stepLabel.Text = "Max Step Height:";
        vbox.AddChild(stepLabel);

        SpinBox stepSpinBox = new SpinBox();
        stepSpinBox.MinValue = 0.1;
        stepSpinBox.MaxValue = 5.0;
        stepSpinBox.Step = 0.1;
        stepSpinBox.Value = 1.0;
        stepSpinBox.ValueChanged += (double val) => onMaxStepHeightChanged((float)val);
        vbox.AddChild(stepSpinBox);

        // Height Control
        var heightLabel = new Label();
        heightLabel.Text = "Vertex Height:";
        vbox.AddChild(heightLabel);

        var heightSpinBox = new SpinBox();
        heightSpinBox.MinValue = -50;
        heightSpinBox.MaxValue = 50;
        heightSpinBox.Step = 0.1;
        heightSpinBox.Editable = false; // disabled until a vertex is selected
        heightSpinBox.ValueChanged += (double val) => onHeightChanged(val);
        vbox.AddChild(heightSpinBox);

        // Deselect button
        var deselectButton = new Button();
        deselectButton.Text = "Deselect All";
        deselectButton.Pressed += () => onDeselectAll();
        vbox.AddChild(deselectButton);

        panel.AddChild(vbox);
        uiRoot.AddChild(panel);

        // --- File Management --- //
        vbox.AddChild(new HSeparator());

        var fileLabel = new Label();
        fileLabel.Text = "File";
        vbox.AddChild(fileLabel);

        // Save directory input
        var dirContainer = new HBoxContainer();
        var dirLabel = new Label();
        dirLabel.Text = "Dir:";
        dirContainer.AddChild(dirLabel);

        var dirInput = new LineEdit();
        dirInput.Text = "res://worlddata/";
        dirInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        dirContainer.AddChild(dirInput);
        vbox.AddChild(dirContainer);

        // Save button
        var saveBtn = new Button();
        saveBtn.Text = "Save All";
        saveBtn.Pressed += () => onSave(dirInput.Text);
        vbox.AddChild(saveBtn);

        // Load Button
        var loadBtn = new Button();
        loadBtn.Text = "Load";
        loadBtn.Pressed += () => onLoad(dirInput.Text);
        vbox.AddChild(loadBtn);

        var chunkCountLabel = new Label();
        chunkCountLabel.Text = "Loaded: 1 Chunk(s)";
        vbox.AddChild(chunkCountLabel);

        return new UiRefs
        {
            HeightSpinBox = heightSpinBox,
            ChunkCountLabel = chunkCountLabel
        };
    }
}
