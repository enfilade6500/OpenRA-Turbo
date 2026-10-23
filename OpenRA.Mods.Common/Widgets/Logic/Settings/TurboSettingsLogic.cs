#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	/// <summary>The Turbo tab of the settings: what this client does differently, and the knobs for it (TURBO.md).</summary>
	public class TurboSettingsLogic : ChromeLogic
	{
		[FluentReference("version")]
		const string Title = "label-turbo-title";

		[FluentReference("fps")]
		const string MinFramerate = "label-turbo-min-framerate";

		[FluentReference]
		const string MinFramerateNever = "label-turbo-min-framerate-never";

		[FluentReference("fps")]
		const string ForceFramerate = "label-turbo-force-framerate";

		[FluentReference]
		const string ForceFramerateOff = "label-turbo-force-framerate-off";

		readonly GraphicSettings ds = Game.Settings.Graphics;
		readonly DebugSettings dbs = Game.Settings.Debug;
		readonly GameSettings gs = Game.Settings.Game;

		[ObjectCreator.UseCtor]
		public TurboSettingsLogic(
			Action<string, string, Func<Widget, Func<bool>>, Func<Widget, Action>> registerPanel,
			string panelID, string label)
		{
			registerPanel(panelID, label, InitPanel, ResetPanel);
		}

		Func<bool> InitPanel(Widget panel)
		{
			var title = FluentProvider.GetMessage(Title, "version", Turbo.Version);
			panel.Get<LabelWidget>("TURBO_TITLE").GetText = () => title;

			// Frames skipped while the simulation is behind, down to this rate
			SettingsUtils.BindIntSliderPref(panel, "MIN_FRAMERATE_SLIDER", ds, "MinFramerate");
			var minFramerateNever = FluentProvider.GetMessage(MinFramerateNever);
			var minFramerateLabel = new CachedTransform<int, string>(fps => FluentProvider.GetMessage(MinFramerate, "fps", fps));
			panel.Get<LabelWidget>("MIN_FRAMERATE_LABEL").GetText = () => ds.MinFramerate >= 25
				? minFramerateNever
				: minFramerateLabel.Update(ds.MinFramerate);

			// A forced low frame rate, to try out what one feels like
			SettingsUtils.BindIntSliderPref(panel, "FORCE_FRAMERATE_SLIDER", dbs, "ForceFramerate");
			var forceFramerateOff = FluentProvider.GetMessage(ForceFramerateOff);
			var forceFramerateLabel = new CachedTransform<int, string>(fps => FluentProvider.GetMessage(ForceFramerate, "fps", fps));
			panel.Get<LabelWidget>("FORCE_FRAMERATE_LABEL").GetText = () => dbs.ForceFramerate > 0
				? forceFramerateLabel.Update(dbs.ForceFramerate)
				: forceFramerateOff;

			// Status bars: the game's own setting, and the three refinements
			var statusBarsDropDown = panel.Get<DropDownButtonWidget>("STATUS_BAR_DROPDOWN");
			statusBarsDropDown.OnMouseDown = _ => DisplaySettingsLogic.ShowStatusBarsDropdown(statusBarsDropDown, gs);
			statusBarsDropDown.GetText = () => DisplaySettingsLogic.StatusBarsLabel(gs.StatusBars);
			SettingsUtils.BindCheckboxPref(panel, "SELECTED_BAR_DAMAGED_CHECKBOX", gs, "SelectedHealthBarOnlyWhenDamaged");
			SettingsUtils.BindCheckboxPref(panel, "DRAG_BOX_BARS_CHECKBOX", gs, "DragBoxStatusBars");
			SettingsUtils.BindCheckboxPref(panel, "NO_INFANTRY_BARS_CHECKBOX", gs, "NoAutomaticStatusBarsOnInfantry");

			// Lays the rows out (they are added before their sizes are known)
			SettingsUtils.AdjustSettingsScrollPanelLayout(panel.Get<ScrollPanelWidget>("SETTINGS_SCROLLPANEL"));

			return () => false;
		}

		Action ResetPanel(Widget panel)
		{
			var dds = new GraphicSettings();
			var ddbs = new DebugSettings();
			var dgs = new GameSettings();
			return () =>
			{
				ds.MinFramerate = dds.MinFramerate;
				dbs.ForceFramerate = ddbs.ForceFramerate;
				gs.StatusBars = dgs.StatusBars;
				gs.SelectedHealthBarOnlyWhenDamaged = dgs.SelectedHealthBarOnlyWhenDamaged;
				gs.DragBoxStatusBars = dgs.DragBoxStatusBars;
				gs.NoAutomaticStatusBarsOnInfantry = dgs.NoAutomaticStatusBarsOnInfantry;
			};
		}
	}
}
