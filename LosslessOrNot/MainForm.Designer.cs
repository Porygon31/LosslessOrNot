namespace LosslessOrNot;

partial class MainForm
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        toolStrip = new ToolStrip();
        addFilesButton = new ToolStripButton();
        addFolderButton = new ToolStripButton();
        toolStripSeparator1 = new ToolStripSeparator();
        analyzeButton = new ToolStripButton();
        cancelButton = new ToolStripButton();
        toolStripSeparator2 = new ToolStripSeparator();
        removeButton = new ToolStripButton();
        clearButton = new ToolStripButton();
        toolStripSeparator3 = new ToolStripSeparator();
        paletteLabel = new ToolStripLabel();
        paletteComboBox = new ToolStripComboBox();
        durationLabel = new ToolStripLabel();
        durationBox = new ToolStripNumericUpDown();
        durationUnitLabel = new ToolStripLabel();
        toolStripSeparator4 = new ToolStripSeparator();
        themeLabel = new ToolStripLabel();
        themeComboBox = new ToolStripComboBox();
        ffmpegButton = new ToolStripButton();
        statusStrip = new StatusStrip();
        summaryLabel = new ToolStripStatusLabel();
        progressBar = new ToolStripProgressBar();
        ffmpegLabel = new ToolStripStatusLabel();
        mainSplit = new SplitContainer();
        fileList = new BufferedListView();
        fileColumn = new ColumnHeader();
        formatColumn = new ColumnHeader();
        sampleRateColumn = new ColumnHeader();
        bitsColumn = new ColumnHeader();
        bitRateColumn = new ColumnHeader();
        contentColumn = new ColumnHeader();
        statusColumn = new ColumnHeader();
        confidenceColumn = new ColumnHeader();
        sourceColumn = new ColumnHeader();
        fileMenu = new ContextMenuStrip(components);
        openLocationMenuItem = new ToolStripMenuItem();
        reanalyzeMenuItem = new ToolStripMenuItem();
        removeMenuItem = new ToolStripMenuItem();
        detailSplit = new SplitContainer();
        detailLayout = new TableLayoutPanel();
        verdictLabel = new Label();
        explanationLabel = new Label();
        confidenceLabel = new Label();
        confidenceBar = new ProgressBar();
        detailsBox = new RichTextBox();
        chartTabs = new TabControl();
        spectrogramTab = new TabPage();
        spectrogramView = new Charts.SpectrogramView();
        spectrumTab = new TabPage();
        spectrumChart = new Charts.SpectrumChart();
        explanationTab = new TabPage();
        explanationBox = new RichTextBox();
        toolStrip.SuspendLayout();
        statusStrip.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)mainSplit).BeginInit();
        mainSplit.Panel1.SuspendLayout();
        mainSplit.Panel2.SuspendLayout();
        mainSplit.SuspendLayout();
        fileMenu.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)detailSplit).BeginInit();
        detailSplit.Panel1.SuspendLayout();
        detailSplit.Panel2.SuspendLayout();
        detailSplit.SuspendLayout();
        detailLayout.SuspendLayout();
        chartTabs.SuspendLayout();
        spectrogramTab.SuspendLayout();
        spectrumTab.SuspendLayout();
        explanationTab.SuspendLayout();
        SuspendLayout();
        //
        // toolStrip
        //
        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.Items.AddRange(new ToolStripItem[] { addFilesButton, addFolderButton, toolStripSeparator1, analyzeButton, cancelButton, toolStripSeparator2, removeButton, clearButton, toolStripSeparator3, durationLabel, durationBox, durationUnitLabel, toolStripSeparator4, paletteLabel, paletteComboBox, themeLabel, themeComboBox, ffmpegButton });
        toolStrip.Location = new Point(0, 0);
        toolStrip.Name = "toolStrip";
        toolStrip.Padding = new Padding(6, 2, 6, 2);
        toolStrip.Size = new Size(1264, 29);
        toolStrip.TabIndex = 0;
        //
        // addFilesButton
        //
        addFilesButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        addFilesButton.Name = "addFilesButton";
        addFilesButton.Size = new Size(124, 22);
        addFilesButton.Text = "Ajouter des fichiers…";
        addFilesButton.ToolTipText = "Ajouter des fichiers audio à analyser (vous pouvez aussi les glisser-déposer sur la fenêtre)";
        addFilesButton.Click += addFilesButton_Click;
        //
        // addFolderButton
        //
        addFolderButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        addFolderButton.Name = "addFolderButton";
        addFolderButton.Size = new Size(124, 22);
        addFolderButton.Text = "Ajouter un dossier…";
        addFolderButton.ToolTipText = "Ajouter tous les fichiers audio d'un dossier, sous-dossiers compris";
        addFolderButton.Click += addFolderButton_Click;
        //
        // toolStripSeparator1
        //
        toolStripSeparator1.Name = "toolStripSeparator1";
        toolStripSeparator1.Size = new Size(6, 25);
        //
        // analyzeButton
        //
        analyzeButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        analyzeButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        analyzeButton.Name = "analyzeButton";
        analyzeButton.Size = new Size(58, 22);
        analyzeButton.Text = "Analyser";
        analyzeButton.ToolTipText = "Analyser les fichiers en attente";
        analyzeButton.Click += analyzeButton_Click;
        //
        // cancelButton
        //
        cancelButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        cancelButton.Enabled = false;
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(53, 22);
        cancelButton.Text = "Annuler";
        cancelButton.ToolTipText = "Interrompre l'analyse en cours";
        cancelButton.Click += cancelButton_Click;
        //
        // toolStripSeparator2
        //
        toolStripSeparator2.Name = "toolStripSeparator2";
        toolStripSeparator2.Size = new Size(6, 25);
        //
        // removeButton
        //
        removeButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        removeButton.Name = "removeButton";
        removeButton.Size = new Size(50, 22);
        removeButton.Text = "Retirer";
        removeButton.ToolTipText = "Retirer les fichiers sélectionnés de la liste (Suppr)";
        removeButton.Click += removeButton_Click;
        //
        // clearButton
        //
        clearButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        clearButton.Name = "clearButton";
        clearButton.Size = new Size(84, 22);
        clearButton.Text = "Vider la liste";
        clearButton.Click += clearButton_Click;
        //
        // toolStripSeparator3
        //
        toolStripSeparator3.Name = "toolStripSeparator3";
        toolStripSeparator3.Size = new Size(6, 25);
        //
        // durationLabel
        //
        durationLabel.Name = "durationLabel";
        durationLabel.Size = new Size(97, 22);
        durationLabel.Text = "Durée analysée :";
        //
        // durationBox
        //
        durationBox.AutoSize = false;
        durationBox.Name = "durationBox";
        durationBox.Size = new Size(56, 23);
        durationBox.ToolTipText = "Secondes analysées dans chaque fichier (5 à 600). Un fichier plus court est analysé en entier.";
        //
        // durationUnitLabel
        //
        durationUnitLabel.Name = "durationUnitLabel";
        durationUnitLabel.Size = new Size(12, 22);
        durationUnitLabel.Text = "s";
        //
        // toolStripSeparator4
        //
        toolStripSeparator4.Name = "toolStripSeparator4";
        toolStripSeparator4.Size = new Size(6, 25);
        //
        // paletteLabel
        //
        paletteLabel.Name = "paletteLabel";
        paletteLabel.Size = new Size(49, 22);
        paletteLabel.Text = "Palette :";
        //
        // paletteComboBox
        //
        paletteComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        paletteComboBox.Items.AddRange(new object[] { "Magma", "Viridis", "Gris" });
        paletteComboBox.Name = "paletteComboBox";
        paletteComboBox.Size = new Size(100, 25);
        paletteComboBox.ToolTipText = "Palette de couleurs du spectrogramme";
        paletteComboBox.SelectedIndexChanged += paletteComboBox_SelectedIndexChanged;
        //
        // themeLabel
        //
        themeLabel.Name = "themeLabel";
        themeLabel.Size = new Size(47, 22);
        themeLabel.Text = "Thème :";
        //
        // themeComboBox
        //
        themeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        themeComboBox.Items.AddRange(new object[] { "Classique", "Sombre" });
        themeComboBox.Name = "themeComboBox";
        themeComboBox.Size = new Size(100, 25);
        themeComboBox.ToolTipText = "Thème de l'interface (appliqué au redémarrage ; le thème sombre demande Windows 11)";
        themeComboBox.SelectedIndexChanged += themeComboBox_SelectedIndexChanged;
        //
        // ffmpegButton
        //
        ffmpegButton.Alignment = ToolStripItemAlignment.Right;
        ffmpegButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        ffmpegButton.Name = "ffmpegButton";
        ffmpegButton.Size = new Size(55, 22);
        ffmpegButton.Text = "ffmpeg…";
        ffmpegButton.ToolTipText = "Indiquer l'emplacement de ffmpeg.exe (ffprobe.exe doit se trouver à côté)";
        ffmpegButton.Click += ffmpegButton_Click;
        //
        // statusStrip
        //
        statusStrip.Items.AddRange(new ToolStripItem[] { summaryLabel, progressBar, ffmpegLabel });
        statusStrip.Location = new Point(0, 739);
        statusStrip.Name = "statusStrip";
        statusStrip.ShowItemToolTips = true;
        statusStrip.Size = new Size(1264, 22);
        statusStrip.TabIndex = 2;
        //
        // summaryLabel
        //
        summaryLabel.Name = "summaryLabel";
        summaryLabel.Size = new Size(927, 17);
        summaryLabel.Spring = true;
        summaryLabel.Text = "Prêt";
        summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // progressBar
        //
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(160, 16);
        progressBar.Visible = false;
        //
        // ffmpegLabel
        //
        ffmpegLabel.Name = "ffmpegLabel";
        ffmpegLabel.Size = new Size(160, 17);
        ffmpegLabel.Text = "ffmpeg : recherche…";
        ffmpegLabel.Click += ffmpegLabel_Click;
        //
        // mainSplit
        //
        mainSplit.Dock = DockStyle.Fill;
        mainSplit.Location = new Point(0, 29);
        mainSplit.Name = "mainSplit";
        mainSplit.Orientation = Orientation.Horizontal;
        //
        // mainSplit.Panel1
        //
        mainSplit.Panel1.Controls.Add(fileList);
        mainSplit.Panel1MinSize = 80;
        //
        // mainSplit.Panel2
        //
        mainSplit.Panel2.Controls.Add(detailSplit);
        mainSplit.Panel2MinSize = 200;
        mainSplit.Size = new Size(1264, 710);
        mainSplit.SplitterDistance = 290;
        mainSplit.SplitterWidth = 6;
        mainSplit.TabIndex = 1;
        //
        // fileList
        //
        fileList.AllowDrop = true;
        fileList.Columns.AddRange(new ColumnHeader[] { fileColumn, formatColumn, sampleRateColumn, bitsColumn, bitRateColumn, contentColumn, statusColumn, confidenceColumn, sourceColumn });
        fileList.ContextMenuStrip = fileMenu;
        fileList.Dock = DockStyle.Fill;
        fileList.FullRowSelect = true;
        fileList.HideSelection = false;
        fileList.Location = new Point(0, 0);
        fileList.Name = "fileList";
        fileList.ShowItemToolTips = true;
        fileList.Size = new Size(1264, 290);
        fileList.TabIndex = 0;
        fileList.UseCompatibleStateImageBehavior = false;
        fileList.View = View.Details;
        fileList.ColumnClick += fileList_ColumnClick;
        fileList.SelectedIndexChanged += fileList_SelectedIndexChanged;
        fileList.DragDrop += files_DragDrop;
        fileList.DragEnter += files_DragEnter;
        fileList.KeyDown += fileList_KeyDown;
        //
        // fileColumn
        //
        fileColumn.Text = "Fichier";
        fileColumn.Width = 320;
        //
        // formatColumn
        //
        formatColumn.Text = "Format";
        formatColumn.Width = 80;
        //
        // sampleRateColumn
        //
        sampleRateColumn.Text = "Fréquence";
        sampleRateColumn.TextAlign = HorizontalAlignment.Right;
        sampleRateColumn.Width = 80;
        //
        // bitsColumn
        //
        bitsColumn.Text = "Bits";
        bitsColumn.TextAlign = HorizontalAlignment.Right;
        bitsColumn.Width = 45;
        //
        // bitRateColumn
        //
        bitRateColumn.Text = "Débit";
        bitRateColumn.TextAlign = HorizontalAlignment.Right;
        bitRateColumn.Width = 80;
        //
        // contentColumn
        //
        contentColumn.Text = "Contenu jusqu'à";
        contentColumn.TextAlign = HorizontalAlignment.Right;
        contentColumn.Width = 105;
        //
        // statusColumn
        //
        statusColumn.Text = "Statut";
        statusColumn.Width = 160;
        //
        // confidenceColumn
        //
        confidenceColumn.Text = "Confiance";
        confidenceColumn.TextAlign = HorizontalAlignment.Right;
        confidenceColumn.Width = 75;
        //
        // sourceColumn
        //
        sourceColumn.Text = "Source probable";
        sourceColumn.Width = 210;
        //
        // fileMenu
        //
        fileMenu.Items.AddRange(new ToolStripItem[] { openLocationMenuItem, reanalyzeMenuItem, removeMenuItem });
        fileMenu.Name = "fileMenu";
        fileMenu.Size = new Size(242, 70);
        fileMenu.Opening += fileMenu_Opening;
        //
        // openLocationMenuItem
        //
        openLocationMenuItem.Name = "openLocationMenuItem";
        openLocationMenuItem.Size = new Size(241, 22);
        openLocationMenuItem.Text = "Ouvrir l'emplacement du fichier";
        openLocationMenuItem.Click += openLocationMenuItem_Click;
        //
        // reanalyzeMenuItem
        //
        reanalyzeMenuItem.Name = "reanalyzeMenuItem";
        reanalyzeMenuItem.Size = new Size(241, 22);
        reanalyzeMenuItem.Text = "Réanalyser";
        reanalyzeMenuItem.Click += reanalyzeMenuItem_Click;
        //
        // removeMenuItem
        //
        removeMenuItem.Name = "removeMenuItem";
        removeMenuItem.ShortcutKeyDisplayString = "Suppr";
        removeMenuItem.Size = new Size(241, 22);
        removeMenuItem.Text = "Retirer de la liste";
        removeMenuItem.Click += removeButton_Click;
        //
        // detailSplit
        //
        detailSplit.Dock = DockStyle.Fill;
        detailSplit.Location = new Point(0, 0);
        detailSplit.Name = "detailSplit";
        //
        // detailSplit.Panel1
        //
        detailSplit.Panel1.Controls.Add(detailLayout);
        detailSplit.Panel1MinSize = 260;
        //
        // detailSplit.Panel2
        //
        detailSplit.Panel2.Controls.Add(chartTabs);
        detailSplit.Panel2MinSize = 300;
        detailSplit.Size = new Size(1264, 414);
        detailSplit.SplitterDistance = 450;
        detailSplit.SplitterWidth = 6;
        detailSplit.TabIndex = 0;
        //
        // detailLayout
        //
        detailLayout.BackColor = SystemColors.Window;
        detailLayout.ColumnCount = 1;
        detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        detailLayout.Controls.Add(verdictLabel, 0, 0);
        detailLayout.Controls.Add(explanationLabel, 0, 1);
        detailLayout.Controls.Add(confidenceLabel, 0, 2);
        detailLayout.Controls.Add(confidenceBar, 0, 3);
        detailLayout.Controls.Add(detailsBox, 0, 4);
        detailLayout.Dock = DockStyle.Fill;
        detailLayout.Location = new Point(0, 0);
        detailLayout.Name = "detailLayout";
        detailLayout.Padding = new Padding(10, 8, 10, 8);
        detailLayout.RowCount = 5;
        detailLayout.RowStyles.Add(new RowStyle());
        detailLayout.RowStyles.Add(new RowStyle());
        detailLayout.RowStyles.Add(new RowStyle());
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        detailLayout.Size = new Size(450, 414);
        detailLayout.TabIndex = 0;
        //
        // verdictLabel
        //
        verdictLabel.AutoSize = true;
        verdictLabel.Dock = DockStyle.Fill;
        verdictLabel.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        verdictLabel.Location = new Point(13, 8);
        verdictLabel.Margin = new Padding(3, 0, 3, 4);
        verdictLabel.Name = "verdictLabel";
        verdictLabel.Size = new Size(424, 28);
        verdictLabel.TabIndex = 0;
        verdictLabel.Text = "Aucun fichier sélectionné";
        //
        // explanationLabel
        //
        explanationLabel.AutoSize = true;
        explanationLabel.Dock = DockStyle.Fill;
        explanationLabel.Location = new Point(13, 40);
        explanationLabel.Margin = new Padding(3, 0, 3, 8);
        explanationLabel.Name = "explanationLabel";
        explanationLabel.Size = new Size(424, 30);
        explanationLabel.TabIndex = 1;
        explanationLabel.Text = "Ajoutez des fichiers audio (boutons ci-dessus ou glisser-déposer) : chacun est analysé pour savoir s'il est vraiment lossless.";
        //
        // confidenceLabel
        //
        confidenceLabel.AutoSize = true;
        confidenceLabel.Dock = DockStyle.Fill;
        confidenceLabel.ForeColor = SystemColors.GrayText;
        confidenceLabel.Location = new Point(13, 78);
        confidenceLabel.Name = "confidenceLabel";
        confidenceLabel.Size = new Size(424, 15);
        confidenceLabel.TabIndex = 2;
        //
        // confidenceBar
        //
        confidenceBar.Dock = DockStyle.Fill;
        confidenceBar.Location = new Point(13, 96);
        confidenceBar.Margin = new Padding(3, 3, 3, 5);
        confidenceBar.Name = "confidenceBar";
        confidenceBar.Size = new Size(424, 14);
        confidenceBar.Style = ProgressBarStyle.Continuous;
        confidenceBar.TabIndex = 3;
        confidenceBar.Visible = false;
        //
        // detailsBox
        //
        detailsBox.BackColor = SystemColors.Window;
        detailsBox.BorderStyle = BorderStyle.None;
        detailsBox.DetectUrls = false;
        detailsBox.Dock = DockStyle.Fill;
        detailsBox.Location = new Point(13, 121);
        detailsBox.Name = "detailsBox";
        detailsBox.ReadOnly = true;
        detailsBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        detailsBox.Size = new Size(424, 282);
        detailsBox.TabIndex = 4;
        detailsBox.Text = "";
        //
        // chartTabs
        //
        chartTabs.Controls.Add(spectrogramTab);
        chartTabs.Controls.Add(spectrumTab);
        chartTabs.Controls.Add(explanationTab);
        chartTabs.Dock = DockStyle.Fill;
        chartTabs.Location = new Point(0, 0);
        chartTabs.Name = "chartTabs";
        chartTabs.SelectedIndex = 0;
        chartTabs.Size = new Size(808, 414);
        chartTabs.TabIndex = 0;
        //
        // spectrogramTab
        //
        spectrogramTab.Controls.Add(spectrogramView);
        spectrogramTab.Location = new Point(4, 24);
        spectrogramTab.Name = "spectrogramTab";
        spectrogramTab.Size = new Size(800, 386);
        spectrogramTab.TabIndex = 0;
        spectrogramTab.Text = "Spectrogramme";
        spectrogramTab.UseVisualStyleBackColor = true;
        //
        // spectrogramView
        //
        spectrogramView.AccessibleName = "Spectrogramme";
        spectrogramView.AccessibleRole = AccessibleRole.Graphic;
        spectrogramView.Dock = DockStyle.Fill;
        spectrogramView.Location = new Point(0, 0);
        spectrogramView.Name = "spectrogramView";
        spectrogramView.Size = new Size(800, 386);
        spectrogramView.TabIndex = 0;
        //
        // spectrumTab
        //
        spectrumTab.Controls.Add(spectrumChart);
        spectrumTab.Location = new Point(4, 24);
        spectrumTab.Name = "spectrumTab";
        spectrumTab.Size = new Size(800, 386);
        spectrumTab.TabIndex = 1;
        spectrumTab.Text = "Spectre moyen";
        spectrumTab.UseVisualStyleBackColor = true;
        //
        // spectrumChart
        //
        spectrumChart.AccessibleName = "Spectre moyen";
        spectrumChart.AccessibleRole = AccessibleRole.Chart;
        spectrumChart.Dock = DockStyle.Fill;
        spectrumChart.Location = new Point(0, 0);
        spectrumChart.Name = "spectrumChart";
        spectrumChart.Size = new Size(800, 386);
        spectrumChart.TabIndex = 0;
        //
        // explanationTab
        //
        explanationTab.BackColor = SystemColors.Window;
        explanationTab.Controls.Add(explanationBox);
        explanationTab.Location = new Point(4, 24);
        explanationTab.Name = "explanationTab";
        explanationTab.Padding = new Padding(10, 8, 6, 6);
        explanationTab.Size = new Size(800, 386);
        explanationTab.TabIndex = 2;
        explanationTab.Text = "Explications";
        //
        // explanationBox
        //
        explanationBox.BackColor = SystemColors.Window;
        explanationBox.BorderStyle = BorderStyle.None;
        explanationBox.DetectUrls = false;
        explanationBox.Dock = DockStyle.Fill;
        explanationBox.Location = new Point(10, 8);
        explanationBox.Name = "explanationBox";
        explanationBox.ReadOnly = true;
        explanationBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        explanationBox.Size = new Size(784, 372);
        explanationBox.TabIndex = 0;
        explanationBox.Text = "";
        //
        // MainForm
        //
        AllowDrop = true;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1264, 761);
        Controls.Add(mainSplit);
        Controls.Add(statusStrip);
        Controls.Add(toolStrip);
        MinimumSize = new Size(900, 600);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "LosslessOrNot — vrai ou faux lossless ?";
        DragDrop += files_DragDrop;
        DragEnter += files_DragEnter;
        toolStrip.ResumeLayout(false);
        toolStrip.PerformLayout();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        mainSplit.Panel1.ResumeLayout(false);
        mainSplit.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)mainSplit).EndInit();
        mainSplit.ResumeLayout(false);
        fileMenu.ResumeLayout(false);
        detailSplit.Panel1.ResumeLayout(false);
        detailSplit.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)detailSplit).EndInit();
        detailSplit.ResumeLayout(false);
        detailLayout.ResumeLayout(false);
        detailLayout.PerformLayout();
        chartTabs.ResumeLayout(false);
        spectrogramTab.ResumeLayout(false);
        spectrumTab.ResumeLayout(false);
        explanationTab.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private ToolStrip toolStrip;
    private ToolStripButton addFilesButton;
    private ToolStripButton addFolderButton;
    private ToolStripSeparator toolStripSeparator1;
    private ToolStripButton analyzeButton;
    private ToolStripButton cancelButton;
    private ToolStripSeparator toolStripSeparator2;
    private ToolStripButton removeButton;
    private ToolStripButton clearButton;
    private ToolStripSeparator toolStripSeparator3;
    private ToolStripLabel paletteLabel;
    private ToolStripComboBox paletteComboBox;
    private ToolStripButton ffmpegButton;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel summaryLabel;
    private ToolStripProgressBar progressBar;
    private ToolStripStatusLabel ffmpegLabel;
    private SplitContainer mainSplit;
    private BufferedListView fileList;
    private ColumnHeader fileColumn;
    private ColumnHeader formatColumn;
    private ColumnHeader sampleRateColumn;
    private ColumnHeader bitsColumn;
    private ColumnHeader bitRateColumn;
    private ColumnHeader contentColumn;
    private ColumnHeader statusColumn;
    private ColumnHeader confidenceColumn;
    private ColumnHeader sourceColumn;
    private ContextMenuStrip fileMenu;
    private ToolStripMenuItem openLocationMenuItem;
    private ToolStripMenuItem reanalyzeMenuItem;
    private ToolStripMenuItem removeMenuItem;
    private SplitContainer detailSplit;
    private TableLayoutPanel detailLayout;
    private Label verdictLabel;
    private Label explanationLabel;
    private Label confidenceLabel;
    private ProgressBar confidenceBar;
    private RichTextBox detailsBox;
    private TabControl chartTabs;
    private TabPage spectrogramTab;
    private Charts.SpectrogramView spectrogramView;
    private TabPage spectrumTab;
    private Charts.SpectrumChart spectrumChart;
    private TabPage explanationTab;
    private RichTextBox explanationBox;
    private ToolStripLabel durationLabel;
    private ToolStripNumericUpDown durationBox;
    private ToolStripLabel durationUnitLabel;
    private ToolStripSeparator toolStripSeparator4;
    private ToolStripLabel themeLabel;
    private ToolStripComboBox themeComboBox;
}
