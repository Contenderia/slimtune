﻿/*
* Copyright (c) 2007-2010 SlimDX Group
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
* THE SOFTWARE.
*/
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using UICore;
using NHibernate;

namespace SlimTuneUI
{
	public partial class ProfilerWindow : ProfilerWindowBase
	{
		SlimTune m_mainWindow;

		public ProfilerWindow(SlimTune mainWindow, Connection conn)
			: base(conn)
		{
			InitializeComponent();
			this.Text = Utilities.GetStandardCaption(conn);
			m_mainWindow = mainWindow;
			m_mainWindow.AddWindow(this);

			Connection.Disconnected += new EventHandler(Connection_Disconnected);
			Connection.DataEngine.DataFlush += new EventHandler(DataEngine_DataFlush);

			string host = string.IsNullOrEmpty(conn.HostName) ? "(file)" : conn.HostName;
			HostLabel.Text = "Host: " + host;
			string port = conn.Port == 0 ? "(file)" : conn.Port.ToString();
			PortLabel.Text = "Port: " + port;
			EngineLabel.Text = "Engine: " + conn.DataEngine.Engine;
			NameLabel.Text = "Name: " + conn.DataEngine.Name;

			string status;
			if(conn.Port == 0)
			{
				status = "Opened From File";
			}
			else if(conn.IsConnected)
			{
				status = "Running";
				SnapshotButton.Enabled = true;
			}
			else
			{
				status = "Stopped";
				ReconnectButton.Enabled = true;
			}
			StatusLabel.Text = "Status: " + status;

			foreach(var vis in Utilities.GetVisualizerList(false))
			{
				m_visualizerCombo.Items.Add(vis);
			}
			m_visualizerCombo.SelectedIndex = 0;

			RefreshSnapshots();
		}

		public override ToolStrip GetToolStrip(IVisualizer visualizer)
		{
			int index = Visualizers.IndexOf(visualizer);
			if(index < 0)
				return null;

			var page = VisualizerHost.TabPages[index];
			var toolbar = page.Controls[0] as VisualizerToolbar;
			return toolbar.ToolStrip;
		}

		void DataEngine_DataFlush(object sender, EventArgs e)
		{
			this.Invoke(new System.Action(RefreshSnapshots));
		}

		void Connection_Disconnected(object sender, EventArgs e)
		{
			try
			{
				if(!this.IsDisposed)
				{
					this.Invoke((System.Action) delegate
					{
						StatusLabel.Text = "Status: Stopped";
						SnapshotButton.Enabled = false;
						ReconnectButton.Enabled = true;
					});
				}
			}
			catch
			{
			}
		}

		private void ProfilerWindow_FormClosed(object sender, FormClosedEventArgs e)
		{
			foreach(var vis in Visualizers)
			{
				vis.OnClose();
			}

			Connection.Dispose();
			Connection = null;
		}

		private void PromptSave()
		{
			SaveFileDialog dlg = new SaveFileDialog();
			dlg.Filter = string.Format("Results file (*.{0})|*.{0}", Connection.DataEngine.Extension);
			dlg.AddExtension = true;

			while(true)
			{
				DialogResult saveResult = dlg.ShowDialog(this);
				if(saveResult == DialogResult.OK)
				{
					try
					{
						Connection.DataEngine.Save(dlg.FileName);
						return;
					}
					catch
					{
						MessageBox.Show("Unable to save results file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
				else
				{
					return;
				}
			}
		}

		private void ProfilerWindow_FormClosing(object sender, FormClosingEventArgs e)
		{
			if(m_mainWindow.IsClosing && !Connection.DataEngine.InMemory)
				return;

			//TODO: Saving of SQLite in-memory databases does not currently work for some reason
			/*if(Connection.StorageEngine.InMemory)
			{
				DialogResult result = MessageBox.Show("Save before exiting?", "Save?",
					MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
				if(result == DialogResult.Yes)
				{
					PromptSave();
				}
				else if(result == DialogResult.Cancel)
				{
					e.Cancel = true;
				}
			}
			else*/
			{
				DialogResult result = MessageBox.Show("Close this connection?", "Close Connection",
					MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
				if(result == DialogResult.No)
				{
					e.Cancel = true;
				}
			}
		}

		private void m_openVisualizerButton_Click(object sender, EventArgs e)
		{
			TypeEntry visEntry = m_visualizerCombo.SelectedItem as TypeEntry;
			if(visEntry != null && visEntry.Type != null)
			{
				AddVisualizer(visEntry.Type);
			}
		}

		public void AddVisualizer(Type visType)
		{
			IVisualizer visualizer = (IVisualizer) Activator.CreateInstance(visType);
			Visualizers.Add(visualizer);
			TabPage page = new TabPage(visualizer.DisplayName);
			page.Tag = visualizer;

			var toolbar = new VisualizerToolbar(ActiveSnapshot, visualizer);
			toolbar.Dock = DockStyle.Top;
			page.Controls.Add(toolbar);

			visualizer.Control.Top = toolbar.Height;
			visualizer.Control.Left = 0;
			visualizer.Control.Size = new System.Drawing.Size(page.Width, page.Height - toolbar.Height);
			visualizer.Control.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			page.Controls.Add(visualizer.Control);

			VisualizerHost.TabPages.Add(page);

			//We have to initialize here to make sure everything is wired if the visualizer calls back in
			if(!visualizer.Initialize(this, Connection, ActiveSnapshot))
			{
				VisualizerHost.TabPages.Remove(page);
				Visualizers.Remove(visualizer);
				return;
			}

			VisualizerHost.SelectedTab = page;
			m_closeVisualizerButton.Enabled = true;
		}

		private void SnapshotButton_Click(object sender, EventArgs e)
		{
			if(!Connection.IsConnected)
			{
				MessageBox.Show("Target must be running in order to take a snapshot.", "Take Snapshot");
				return;
			}

			Connection.DataEngine.Snapshot("User");
			RefreshSnapshots();
			MessageBox.Show("Snapshot saved", "Take Snapshot");
		}

		private void RefreshSnapshots()
		{
			int selId = 0, selIndex = 0;
			if(SnapshotsListBox.CheckedItems.Count > 0)
				selId = (SnapshotsListBox.CheckedItems[0] as Snapshot).Id;

			SnapshotsListBox.Items.Clear();
			using(var session = Connection.DataEngine.OpenSession())
			using(var tx = session.BeginTransaction())
			{
				var snapshots = session.CreateCriteria<Snapshot>()
					.AddOrder(NHibernate.Criterion.Order.Desc("TimeStamp"))
					.List<Snapshot>();
				foreach(var snap in snapshots)
				{
					if(snap.Id == selId)
						selIndex = SnapshotsListBox.Items.Count;

					if(snap.Id == 0)
						SnapshotsListBox.Items.Insert(0, snap);
					else
						SnapshotsListBox.Items.Add(snap);
				}

				tx.Commit();
			}

			SnapshotsListBox.SetItemChecked(selIndex, true);
			ActiveSnapshot = SnapshotsListBox.CheckedItems[0] as Snapshot;
		}

		private void CloseTab(int index)
		{
			var tab = VisualizerHost.TabPages[index];

			if(tab == VisualizerHost.SelectedTab)
			{
				//Select the next tab to the right, or the rightmost tab
				if(VisualizerHost.TabPages.Count > index + 1)
					VisualizerHost.SelectedIndex = index + 1;
				else if(VisualizerHost.TabPages.Count > 1)
					VisualizerHost.SelectedIndex = VisualizerHost.TabPages.Count - 2;
			}

			//close down the visualizer and its associated tab
			var vis = (IVisualizer) tab.Tag;
			vis.OnClose();
			Visualizers.Remove(vis);
			VisualizerHost.TabPages.Remove(tab);

			Debug.Assert(Visualizers.Count == VisualizerHost.TabPages.Count);

			if(VisualizerHost.TabPages.Count == 0)
			{
				m_closeVisualizerButton.Enabled = false;
			}
		}

		private void m_closeVisualizerButton_Click(object sender, EventArgs e)
		{
			var tab = VisualizerHost.SelectedTab;
			int index = VisualizerHost.SelectedIndex;
			if(tab != null)
			{
				CloseTab(index);
			}
		}

		private void VisualizerHost_MouseClick(object sender, MouseEventArgs e)
		{
			if(e.Button == MouseButtons.Middle)
			{
				Point pos = e.Location;
				for(int i = 0; i < VisualizerHost.TabPages.Count; ++i)
				{
					if(VisualizerHost.GetTabRect(i).Contains(e.Location))
					{
						CloseTab(i);
						break;
					}
				}
			}
		}

		private void SuspendButton_Click(object sender, EventArgs e)
		{
			if(Connection.Client != null)
				Connection.Client.SuspendTarget();
		}

		private void ResumeButton_Click(object sender, EventArgs e)
		{
			if(Connection.Client != null)
				Connection.Client.ResumeTarget();
		}

		private void PauseButton_Click(object sender, EventArgs e)
		{
			Connection.Client.SetSamplerActive(false);
		}

		private void SnapshotsListBox_Format(object sender, ListControlConvertEventArgs e)
		{
			var item = e.ListItem as Snapshot;
			e.Value = string.Format("({0}) {1} - {2} {3}", item.Id, item.Name, item.DateTime.ToLongTimeString(), item.DateTime.ToShortDateString());
		}

		private void SnapshotsListBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			for(int i = 0; i < SnapshotsListBox.Items.Count; ++i)
			{
				SnapshotsListBox.SetItemChecked(i, false);
			}

			SnapshotsListBox.SetItemChecked(SnapshotsListBox.SelectedIndex, true);
			ActiveSnapshot = SnapshotsListBox.SelectedItem as Snapshot;
			RenameSnapshotButton.Enabled = ActiveSnapshot.Id != 0;
		}

		private void ReconnectButton_Click(object sender, EventArgs e)
		{
			ConnectProgress progress = new ConnectProgress(Connection.HostName, Connection.Port, Connection.DataEngine, 10);
			progress.ShowDialog(this);

			if(progress.Client != null)
			{
				Connection.RunClient(progress.Client);
				ReconnectButton.Enabled = false;
				StatusLabel.Text = "Status: Running";
				this.BringToFront();
			}
		}

		private void DeleteSnapshotButton_Click(object sender, EventArgs e)
		{
			var snapshot = SnapshotsListBox.SelectedItem as Snapshot;
			if(snapshot.Id == 0)
			{
				DialogResult result = MessageBox.Show("WARNING: This will clear all currently collected data. Save snapshot before continuing?",
					"Clear Data", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button3);
				if(result == DialogResult.Yes)
				{
					Connection.DataEngine.Snapshot("Cleared Data");
					RefreshSnapshots();
					Connection.DataEngine.ClearData();
				}
				else if(result == DialogResult.No)
				{
					Connection.DataEngine.ClearData();
				}
			}
			else
			{
				DialogResult result = MessageBox.Show(string.Format("Are you sure you want to delete '{0}', saved on {1}?", snapshot.Name, snapshot.DateTime),
					"Delete Snapshot", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2);
				if(result == DialogResult.Yes)
				{
					//delete the snapshot
					using(var session = Connection.DataEngine.OpenSession())
					using(var tx = session.BeginTransaction())
					{
						session.Lock(snapshot, LockMode.None);

						//have not figured out how to make this automatic yet
						session.CreateQuery("delete from Call where SnapshotId = :id")
							.SetInt32("id", snapshot.Id)
							.ExecuteUpdate();
						session.CreateQuery("delete from Sample where SnapshotId = :id")
							.SetInt32("id", snapshot.Id)
							.ExecuteUpdate();

						session.Delete(snapshot);

						tx.Commit();
					}

					RefreshSnapshots();

					//close visualizers using the snapshot
					for(int t = 0; t < VisualizerHost.TabPages.Count;)
					{
						var page = VisualizerHost.TabPages[t];
						var vis = page.Tag as IVisualizer;
						if(vis.Snapshot.Id == snapshot.Id)
						{
							CloseTab(t);
						}
						else
						{
							++t;
						}
					}
				}
			}
		}

		private void RenameSnapshotButton_Click(object sender, EventArgs e)
		{
			if(ActiveSnapshot.Id == 0)
			{
				//TODO: Inform the user?
				return;
			}

			string name = string.Empty;
			var result = Utilities.InputBox("Rename Snapshot",
				string.Format("Enter the new name for '{0}', saved on {1}:", ActiveSnapshot.Name, ActiveSnapshot.DateTime),
				ref name);
			if(result == System.Windows.Forms.DialogResult.Cancel || name == string.Empty)
				return;

			using(var session = Connection.DataEngine.OpenSession())
			using(var tx = session.BeginTransaction())
			{
				session.Lock(ActiveSnapshot, LockMode.None);
				ActiveSnapshot.Name = name;
				session.Update(ActiveSnapshot);

				tx.Commit();
			}
			RefreshSnapshots();
		}
	}
}
