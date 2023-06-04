using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Bolt;
using I2.Loc;
using RCG.Rollback.Components;
using RCG.Rollback.Components.Environment;
using RCG.Rollback.Components.Meta;
using RCG.Rollback.Components.Minigames;
using RCG.Rollback.Events;
using RCG.Rollback.Events.Meta;
using RCG.Rollback.Systems;
using RCG.UI;
using RCG.UI.Screens;
using Tools.BinaryPackets;
using Tools.BinaryRollback;
using UdpKit;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using wfExternals;

namespace RCG.Rollback
{
	// Token: 0x02000644 RID: 1604
	[BoltGlobalBehaviour]
	public class BoltLinker : GlobalEventListener
	{
		// Token: 0x06002EE1 RID: 12001 RVA: 0x0000301E File Offset: 0x0000121E
		public override bool PersistBetweenStartupAndShutdown()
		{
			return true;
		}

		// Token: 0x14000020 RID: 32
		// (add) Token: 0x06002EE2 RID: 12002 RVA: 0x000D33D8 File Offset: 0x000D15D8
		// (remove) Token: 0x06002EE3 RID: 12003 RVA: 0x000D340C File Offset: 0x000D160C
		public static event Action OnBoltShutdown;

		// Token: 0x06002EE4 RID: 12004 RVA: 0x000D3440 File Offset: 0x000D1640
		static BoltLinker()
		{
			RCGRollback.OnBackgroundCaughtUp += BoltLinker.SyncFinished;
			for (int i = 0; i < 60; i++)
			{
				BoltLinker.stats.Add(default(SimulationStats));
			}
		}

		// Token: 0x1700095A RID: 2394
		// (get) Token: 0x06002EE5 RID: 12005 RVA: 0x0001ECC1 File Offset: 0x0001CEC1
		public static uint MyConnectionID
		{
			get
			{
				BoltConnection server = BoltNetwork.Server;
				if (server == null)
				{
					return uint.MaxValue;
				}
				return server.ConnectionId;
			}
		}

		// Token: 0x06002EE6 RID: 12006 RVA: 0x0001ECD3 File Offset: 0x0001CED3
		private static void PushStat(SimulationStats stat)
		{
			BoltLinker.stats[BoltLinker.current] = stat;
			BoltLinker.current = (BoltLinker.current + 1) % 60;
		}

		// Token: 0x06002EE7 RID: 12007 RVA: 0x000D355C File Offset: 0x000D175C
		public static SimulationStats GetSimulationStats()
		{
			SimulationStats simulationStats = default(SimulationStats);
			for (int i = 0; i < 60; i++)
			{
				simulationStats += BoltLinker.stats[i];
			}
			return simulationStats / 60f;
		}

		// Token: 0x06002EE8 RID: 12008 RVA: 0x000D359C File Offset: 0x000D179C
		public static void UpdateControllerEntities(SimulationIteration iteration, SaveSlotEntity localSaveSlot)
		{
			List<PlayerControllerEntity> list = localSaveSlot.ReadOnlyControllers(iteration);
			for (int i = 0; i < list.Count; i++)
			{
				for (int j = 0; j < BoltLinker.m_localControllers.Count; j++)
				{
					if (BoltLinker.m_localControllers[j].LocalPlayerID == (int)list[i].m_playerData.m_localPlayerID)
					{
						BoltLinker.LocalControllerData localControllerData = BoltLinker.m_localControllers[j];
						localControllerData.m_controllerSimualtionID = list[i].NetID;
						IPlayerControlled playerControlled = list[i].Player(iteration);
						localControllerData.m_playerEntityID = ((playerControlled != null) ? playerControlled.NetID : uint.MaxValue);
						break;
					}
				}
			}
		}

		// Token: 0x06002EE9 RID: 12009 RVA: 0x000D363C File Offset: 0x000D183C
		public static int LocalID(uint controllerID)
		{
			for (int i = 0; i < BoltLinker.m_localControllers.Count; i++)
			{
				if (BoltLinker.m_localControllers[i].m_controllerSimualtionID == controllerID)
				{
					return BoltLinker.m_localControllers[i].LocalPlayerID;
				}
			}
			return -1;
		}

		// Token: 0x06002EEA RID: 12010 RVA: 0x000D3684 File Offset: 0x000D1884
		public override void BoltShutdownBegin(AddCallback registerDoneCallback, UdpConnectionDisconnectReason disconnectReason)
		{
			RumbleSystem.StopAllRumbles();
			global::Singleton<InputManager>.instance.DisconnectAllControllers();
			global::Singleton<InputManager>.instance.SetControllerLimit(1);
			UI_CellPhone_Inventory.m_reactedFlairs.Clear();
			UI_HonkrList.m_reactedToHonks.Clear();
			BoltLinker.PacketID = -1;
			using (SimulationIteration mainIterator = RCGRollback.MainIterator)
			{
				ServerStateEntity serverStateEntity = ServerStateEntity.Singleton(mainIterator);
				if (mainIterator.GetFirst<HideoutEntity>() != null && mainIterator.GetFirst<MinigameEntity>() == null)
				{
					ProfileManager.Profile.ActiveSlot.ReadFromSimulation(mainIterator);
					ProfileManager.Profile.ActiveSlot.ReadFromSimulationServer(mainIterator, BoltLinker.GetSceneNameFromIndex(serverStateEntity.m_serverData.LastSceneBuildIndex), BoltLinker.GetSceneNameFromIndex(serverStateEntity.m_serverData.CurrentSceneBuildIndex));
				}
			}
			if (UserProfileManager.HasInstance())
			{
				UserProfileManager.Instance.StopOnlinePlay();
			}
			if (BoltLinker.OnBoltShutdown != null)
			{
				BoltLinker.OnBoltShutdown();
			}
			RCGRollback.Clear();
			BoltLinker.isReturningFromSleep = false;
			BoltLinker.InternetConnection = true;
			if (!BoltLinker.InitiatedDisconnect)
			{
				UI_Lobby lobby = global::Singleton<ScreenManager>.instance.Find<UI_Lobby>(false);
				UI_Loading ui_Loading = global::Singleton<ScreenManager>.instance.Find<UI_Loading>(false);
				if (ui_Loading)
				{
					ui_Loading.HasPriority = false;
				}
				if (!lobby && SceneManager.GetActiveScene().name != "MainMenu")
				{
					MusicManager.instance.StopLevelMusic(0f);
					RCGAudio.instance.KillAllEventInstances(null);
				}
				if (!BoltNetwork.IsServer)
				{
					if (BoltLinker.Kicked)
					{
						global::Singleton<ScreenManager>.instance.AddMenu<UI_Confirmation>(UI_Confirmation.CreateData(LocalizationManager.GetTermTranslation("Sheet1/KickedFromGame", true, 0, true, false, null, null, true), LocalizationManager.GetTermTranslation("Sheet1/UI_DansuAlert_Ok", true, 0, true, false, null, null, true), delegate(bool b)
						{
							this.ConfirmShutdown(null);
							BoltLinker.Kicked = false;
						}), false);
						return;
					}
					if (BoltLinker.Blocked)
					{
						global::Singleton<ScreenManager>.instance.AddMenu<UI_Confirmation>(UI_Confirmation.CreateData(LocalizationManager.GetTermTranslation("Sheet1/BlockedFromGame", true, 0, true, false, null, null, true), LocalizationManager.GetTermTranslation("Sheet1/UI_DansuAlert_Ok", true, 0, true, false, null, null, true), delegate(bool b)
						{
							this.ConfirmShutdown(null);
							BoltLinker.Blocked = false;
						}), false);
						return;
					}
					if (!BoltLinker.InternetConnection)
					{
						if (lobby)
						{
							global::Singleton<ScreenManager>.instance.AddMenu<UI_Confirmation>(UI_Confirmation.CreateData(LocalizationManager.GetTermTranslation("Sheet1/Popup_NetworkDisconnect", true, 0, true, false, null, null, true), LocalizationManager.GetTermTranslation("Sheet1/UI_DansuAlert_Ok", true, 0, true, false, null, null, true), delegate(bool b)
							{
								this.ConfirmShutdown(lobby);
							}), false);
							return;
						}
						global::Singleton<ScreenManager>.instance.AddMenu<UI_Confirmation>(UI_Confirmation.CreateData(LocalizationManager.GetTermTranslation("Sheet1/Popup_NetworkDisconnect", true, 0, true, false, null, null, true), LocalizationManager.GetTermTranslation("Sheet1/UI_DansuAlert_Ok", true, 0, true, false, null, null, true), delegate(bool b)
						{
							this.ConfirmShutdown(null);
						}), false);
						return;
					}
					else
					{
						if (lobby)
						{
							global::Singleton<ScreenManager>.instance.AddMenu<UI_Confirmation>(UI_Confirmation.CreateData(LocalizationManager.GetTermTranslation("Sheet1/Popup_HostDisconnect", true, 0, true, false, null, null, true), LocalizationManager.GetTermTranslation("Sheet1/UI_DansuAlert_Ok", true, 0, true, false, null, null, true), delegate(bool b)
							{
								this.ConfirmShutdown(lobby);
							}), false);
							return;
						}
						global::Singleton<ScreenManager>.instance.AddMenu<UI_Confirmation>(UI_Confirmation.CreateData(LocalizationManager.GetTermTranslation("Sheet1/Popup_HostDisconnect", true, 0, true, false, null, null, true), LocalizationManager.GetTermTranslation("Sheet1/UI_DansuAlert_Ok", true, 0, true, false, null, null, true), delegate(bool b)
						{
							this.ConfirmShutdown(null);
						}), false);
						return;
					}
				}
				else
				{
					if (lobby)
					{
						global::Singleton<ScreenManager>.instance.AddMenu<UI_Confirmation>(UI_Confirmation.CreateData(LocalizationManager.GetTermTranslation("Sheet1/Popup_NetworkDisconnect", true, 0, true, false, null, null, true), LocalizationManager.GetTermTranslation("Sheet1/UI_DansuAlert_Ok", true, 0, true, false, null, null, true), delegate(bool b)
						{
							this.ConfirmShutdown(lobby);
						}), false);
						return;
					}
					global::Singleton<ScreenManager>.instance.AddMenu<UI_Confirmation>(UI_Confirmation.CreateData(LocalizationManager.GetTermTranslation("Sheet1/Popup_NetworkDisconnect", true, 0, true, false, null, null, true), LocalizationManager.GetTermTranslation("Sheet1/UI_DansuAlert_Ok", true, 0, true, false, null, null, true), delegate(bool b)
					{
						this.ConfirmShutdown(null);
					}), false);
				}
			}
		}

		// Token: 0x06002EEB RID: 12011 RVA: 0x0001ECF4 File Offset: 0x0001CEF4
		private void ConfirmShutdown(UI_Lobby lobby = null)
		{
			if (lobby)
			{
				lobby.LeaveLobby();
			}
			else
			{
				SceneManager.LoadScene("MainMenu");
			}
			BoltLinker.LoggedOff = false;
		}

		// Token: 0x06002EEC RID: 12012 RVA: 0x0001ED16 File Offset: 0x0001CF16
		public override void BoltStartFailed(UdpConnectionDisconnectReason disconnectReason)
		{
			base.StartCoroutine(this.WaitForBoltExit(disconnectReason));
		}

		// Token: 0x06002EED RID: 12013 RVA: 0x0001ED26 File Offset: 0x0001CF26
		private IEnumerator WaitForBoltExit(UdpConnectionDisconnectReason disconnectReason)
		{
			BoltNetwork.Shutdown();
			while (BoltNetwork.IsRunning)
			{
				yield return 0;
			}
			yield break;
		}

		// Token: 0x06002EEE RID: 12014 RVA: 0x000D3A40 File Offset: 0x000D1C40
		public override void Disconnected(BoltConnection connection)
		{
			base.Disconnected(connection);
			if (BoltNetwork.IsServer)
			{
				BoltLinker.pings.Remove(connection);
			}
			if (BoltNetwork.IsServer)
			{
				BoltLinker.AddEvent<DeleteSaveSlotEvent>(new DeleteSaveSlotEvent
				{
					ConnectionID = connection.ConnectionId
				}, -1);
				return;
			}
			if (BoltNetwork.Server == connection)
			{
				global::Singleton<InputManager>.instance.OnLocalPlayerAdded -= this.CheckLocalPlayer;
				global::Singleton<InputManager>.instance.OnLocalPlayerRemoved -= this.RemoveLocalPlayer;
			}
		}

		// Token: 0x06002EEF RID: 12015 RVA: 0x0001ED2E File Offset: 0x0001CF2E
		public override void SessionListUpdated(Map<Guid, UdpSession> sessionList)
		{
			BoltLinker.SessionUpdateEvent sessionListUpdatedEvent = BoltLinker.SessionListUpdatedEvent;
			if (sessionListUpdatedEvent != null)
			{
				sessionListUpdatedEvent(sessionList);
			}
			base.SessionListUpdated(sessionList);
		}

		// Token: 0x06002EF0 RID: 12016 RVA: 0x000D3ABC File Offset: 0x000D1CBC
		public override void Connected(BoltConnection connection)
		{
			if (BoltNetwork.IsClient)
			{
				this.InputFrame = -1;
			}
			base.Connected(connection);
			if (BoltNetwork.IsServer)
			{
				BoltLinker.pings.Add(connection, new BoltLinker.ConnectionData());
			}
			if (connection == BoltNetwork.Server)
			{
				global::Singleton<ScreenManager>.instance.AddMenu<UI_Loading>(UI_Loading.DownloadMetaState(), false);
			}
		}

		// Token: 0x06002EF1 RID: 12017 RVA: 0x0001ED48 File Offset: 0x0001CF48
		public override void OnEvent(Sync_Request evnt)
		{
			BoltLinker.SendGameState(evnt.RaisedBy, false, evnt.ID);
		}

		// Token: 0x06002EF2 RID: 12018 RVA: 0x0001ED5C File Offset: 0x0001CF5C
		public override void OnEvent(Meta_Request evnt)
		{
			BoltLinker.SendMetaState(evnt.RaisedBy, false, evnt.ID);
		}

		// Token: 0x06002EF3 RID: 12019 RVA: 0x000D3B10 File Offset: 0x000D1D10
		public override void OnEvent(Meta_Checksum evnt)
		{
			this.m_metaPlayer = null;
			RCGRollback.MetaIteration.GetEntities<MetaPlayerState>(out this.m_metaPlayers);
			for (int i = 0; i < this.m_metaPlayers.RestrictedCount; i++)
			{
				if (this.m_metaPlayers[i].m_connectionID == evnt.RaisedBy.ConnectionId)
				{
					this.m_metaPlayer = this.m_metaPlayers[i];
				}
			}
			if (this.m_metaPlayer == null || this.m_metaPlayer.m_desynced_meta)
			{
				return;
			}
			int frame = evnt.Frame;
			int num = Mathf.Min(frame, BoltLinker.m_mainMetaTimeFrames.MaxSimulated);
			int num2 = BoltLinker.m_mainMetaTimeFrames.MaxSimulated - num;
			if (num2 >= 100)
			{
				Debug.LogError(string.Concat(new object[]
				{
					"Metas checksum to far in past or future ",
					num2,
					" ",
					num,
					" ",
					frame,
					" ",
					BoltLinker.m_mainMetaTimeFrames.MaxSimulated,
					" ",
					BoltLinker.m_mainMetaTimeFrames.CurrentSimulate
				}));
				return;
			}
			byte[] metaRaw = RCGRollback.GetMetaRaw(frame, true);
			if (metaRaw == null)
			{
				Debug.LogError(string.Concat(new object[]
				{
					"Unable to send frame ",
					frame,
					" - too far in the past or future. Current Frame ",
					BoltLinker.m_mainMetaTimeFrames.CurrentSimulate
				}));
				return;
			}
			int num3 = 0;
			ulong num4 = metaRaw.ToUInt64(ref num3);
			num3 = 0;
			this.ChecksumBuilder.Include(evnt.Int1, ref num3);
			this.ChecksumBuilder.Include(evnt.Int2, ref num3);
			num3 = 0;
			ulong num5 = this.ChecksumBuilder.ToUInt64(ref num3);
			if (num4 != num5)
			{
				BoltLinker.AddMetaEvent<SetMetaPlayerDesyncedMeta>(new SetMetaPlayerDesyncedMeta
				{
					m_connectionID = evnt.RaisedBy.ConnectionId,
					m_desynced_meta = true
				}, -1);
			}
			BoltLinker.ConnectionData connectionData = BoltLinker.pings[evnt.RaisedBy];
			connectionData.UpdatePlayer(BoltLinker.pings.Count == 1);
			SetDelay.Post(evnt.RaisedBy, connectionData.m_lastPing, connectionData.m_lastDelaySent);
			float num6;
			if (BoltLinker.pings.Count == 1)
			{
				num6 = Mathf.Clamp((connectionData.m_lastPing - 0.05f) / 0.15f, 0f, 1f);
			}
			else
			{
				num6 = Mathf.Clamp((connectionData.m_lastPing / 2f - 0.05f) / 0.15f, 0f, 1f);
			}
			num6 = 1f - num6;
			BoltLinker.AddEvent<SetConnectionStrength>(new SetConnectionStrength
			{
				m_connectionID = evnt.RaisedBy.ConnectionId,
				m_strength = num6
			}, BoltLinker.m_mainMetaTimeFrames.MaxSimulated + RCGRollback.System.FPS / 2);
			if (BoltLinker.pings.Count > 1)
			{
				int num7 = 0;
				foreach (KeyValuePair<BoltConnection, BoltLinker.ConnectionData> keyValuePair in BoltLinker.pings)
				{
					num7 += keyValuePair.Value.m_lastDelaySent;
				}
				num7 /= BoltLinker.pings.Count;
				this.ServerDelay = num7 / 2;
				return;
			}
			if (BoltLinker.pings.Count == 1)
			{
				this.ServerDelay = connectionData.m_lastDelaySent;
				if (this.ServerDelay > 1)
				{
					this.ServerDelay--;
					return;
				}
			}
			else
			{
				this.ServerDelay = 0;
			}
		}

		// Token: 0x06002EF4 RID: 12020 RVA: 0x000D3E88 File Offset: 0x000D2088
		public override void OnEvent(Sync_Checksum evnt)
		{
			this.m_metaPlayer = null;
			RCGRollback.MetaIteration.GetEntities<MetaPlayerState>(out this.m_metaPlayers);
			for (int i = 0; i < this.m_metaPlayers.RestrictedCount; i++)
			{
				if (this.m_metaPlayers[i].m_connectionID == evnt.RaisedBy.ConnectionId)
				{
					this.m_metaPlayer = this.m_metaPlayers[i];
				}
			}
			if (this.m_metaPlayer == null || this.m_metaPlayer.m_desynced_main)
			{
				return;
			}
			for (int j = 0; j < this.m_metaPlayers.RestrictedCount; j++)
			{
				if (this.m_metaPlayers[j].m_connectionID == BoltLinker.MyConnectionID)
				{
					this.m_metaPlayer = this.m_metaPlayers[j];
				}
			}
			if (this.m_metaPlayer.m_loading)
			{
				return;
			}
			int frame = evnt.Frame;
			int num = Mathf.Min(frame, RCGRollback.MainTimeFrames.MaxSimulated);
			int num2 = RCGRollback.MainTimeFrames.MaxSimulated - num;
			if (num2 >= 120)
			{
				Debug.LogError(string.Concat(new object[]
				{
					"Main checksum to far in past or future ",
					num2,
					" ",
					num,
					" ",
					frame,
					" ",
					RCGRollback.MainTimeFrames.MaxSimulated,
					" ",
					RCGRollback.MainTimeFrames.CurrentSimulate
				}));
				return;
			}
			byte[] raw = RCGRollback.GetRaw(frame, true);
			if (raw == null)
			{
				Debug.LogError(string.Concat(new object[]
				{
					"Unable to send frame ",
					frame,
					" - too far in the past or future. Current Frame ",
					RCGRollback.MainTimeFrames.CurrentSimulate
				}));
				return;
			}
			int num3 = 0;
			ulong num4 = raw.ToUInt64(ref num3);
			num3 = 0;
			this.ChecksumBuilder.Include(evnt.Int1, ref num3);
			this.ChecksumBuilder.Include(evnt.Int2, ref num3);
			num3 = 0;
			ulong num5 = this.ChecksumBuilder.ToUInt64(ref num3);
			if (num4 != num5)
			{
				BoltLinker.AddMetaEvent<SetMetaPlayerDesyncedMain>(new SetMetaPlayerDesyncedMain
				{
					m_connectionID = evnt.RaisedBy.ConnectionId,
					m_desynced_main = true
				}, -1);
			}
		}

		// Token: 0x06002EF5 RID: 12021 RVA: 0x000D40BC File Offset: 0x000D22BC
		public static void SendGameState(BoltConnection connection, bool forceSave, int ID)
		{
			int num = Mathf.Max(RCGRollback.MainTimeFrames.MinimumFrame, RCGRollback.MainTimeFrames.MaxSimulated - 10);
			byte[] raw = RCGRollback.GetRaw(num, true);
			if (raw == null)
			{
				Debug.LogError(string.Concat(new object[]
				{
					"BoltLinker::SendGameState Huge desync issue min[",
					RCGRollback.MainTimeFrames.MinimumFrame,
					"] L[",
					RCGRollback.MainTimeFrames.MaxSimulated,
					"]"
				}));
				return;
			}
			int num2 = 0;
			num2 = 16;
			int num3 = raw.ToInt32(ref num2);
			Array.Copy(raw, 0, BoltLinker.m_incomingBytes, 12, num3);
			BinaryPacket outGoing = new BinaryPacket(BoltLinker.m_incomingBytes);
			int num4 = num3 + 12;
			outGoing.WriteInt32(num);
			outGoing.WriteInt32(ID);
			outGoing.WriteInt32(num4);
			outGoing.Position = num4 + 4;
			int c = 0;
			new List<EventEntity>();
			RCGRollback.System.GetCurrentEventsAfterFrame(num, delegate(int f, EventEntity ee)
			{
				int c2 = c;
				c = c2 + 1;
				outGoing.WriteInt32(f);
				ee.Serialize(outGoing);
			});
			num3 = outGoing.Position;
			outGoing.Position = num4;
			outGoing.WriteInt32(c);
			Array.Copy(BoltLinker.m_incomingBytes, 0, BoltLinker.evtWriteBuffer, 0, num3);
			BoltLinker.SendRawDataThroughNetwork(BoltLinker.evtWriteBuffer, num3, BoltLinker.PacketType.GameState, connection);
		}

		// Token: 0x06002EF6 RID: 12022 RVA: 0x000D4220 File Offset: 0x000D2420
		public static void SendMetaState(BoltConnection connection, bool forceSave, int ID)
		{
			int num = Mathf.Max(BoltLinker.m_mainMetaTimeFrames.MinimumFrame, BoltLinker.m_mainMetaTimeFrames.MaxSimulated - 10);
			byte[] metaRaw = RCGRollback.GetMetaRaw(num, true);
			if (metaRaw == null)
			{
				Debug.LogError(string.Concat(new object[]
				{
					"BoltLinker::SendGameState Huge desync issue min[",
					BoltLinker.m_mainMetaTimeFrames.MinimumFrame,
					"] L[",
					BoltLinker.m_mainMetaTimeFrames.MaxSimulated,
					"]"
				}));
				return;
			}
			EntityLog entityLog = new EntityLog
			{
				m_counter = new EntityLog.Counter(),
				m_data = "Meta send"
			};
			SimulationIteration simulationIteration = new SimulationIteration(RCGRollback.MetaSystem, 1024);
			SimulationFrame simulationFrame = new SimulationFrame(1024)
			{
				Raw = metaRaw
			};
			simulationIteration.Read(simulationFrame);
			IRestrictedList<IOnPrepareFrame> restrictedList;
			simulationIteration.GetEntities<IOnPrepareFrame>(out restrictedList);
			for (int i = 0; i < restrictedList.RestrictedCount; i++)
			{
				restrictedList[i].OnPrepareFrame(simulationIteration);
			}
			simulationIteration.FillLog(entityLog);
			Debug.Log("Sending \n" + entityLog);
			int num2 = 0;
			num2 = 16;
			int num3 = metaRaw.ToInt32(ref num2);
			Array.Copy(metaRaw, 0, BoltLinker.m_incomingBytes, 12, num3);
			BinaryPacket outGoing = new BinaryPacket(BoltLinker.m_incomingBytes);
			int num4 = num3 + 12;
			outGoing.WriteInt32(num);
			outGoing.WriteInt32(ID);
			outGoing.WriteInt32(num4);
			outGoing.Position = num4 + 4;
			int c = 0;
			new List<EventEntity>();
			RCGRollback.MetaSystem.GetCurrentEventsAfterFrame(num, delegate(int f, EventEntity ee)
			{
				int c2 = c;
				c = c2 + 1;
				outGoing.WriteInt32(f);
				ee.Serialize(outGoing);
			});
			num3 = outGoing.Position;
			outGoing.Position = num4;
			outGoing.WriteInt32(c);
			Array.Copy(BoltLinker.m_incomingBytes, 0, BoltLinker.evtWriteBuffer, 0, num3);
			Debug.LogError("Sending Meta State");
			BoltLinker.SendRawDataThroughNetwork(BoltLinker.evtWriteBuffer, num3, BoltLinker.PacketType.MetaState, connection);
		}

		// Token: 0x1700095B RID: 2395
		// (get) Token: 0x06002EF7 RID: 12023 RVA: 0x0001ED70 File Offset: 0x0001CF70
		public int CurrentInputDelay
		{
			get
			{
				return Mathf.Max(this.ServerDelay, BoltLinker.UserDelay);
			}
		}

		// Token: 0x06002EF8 RID: 12024 RVA: 0x0001ED82 File Offset: 0x0001CF82
		public override void OnEvent(SetDelay evnt)
		{
			this.ServerDelay = evnt.Delay;
			base.OnEvent(evnt);
		}

		// Token: 0x06002EF9 RID: 12025 RVA: 0x0001ED97 File Offset: 0x0001CF97
		public static IEnumerator SynchronizeTime()
		{
			BoltLinker.samples.Clear();
			while (BoltLinker.samples.Count < 10)
			{
				Simulation.ServerTimeSample serverTimeSample = new Simulation.ServerTimeSample();
				if (!BoltLinker.samples.ContainsKey(serverTimeSample.ID))
				{
					BoltLinker.samples.Add(serverTimeSample.ID, serverTimeSample);
					TimeSync_Request.Post(GlobalTargets.OnlyServer, ReliabilityModes.ReliableOrdered, serverTimeSample.ID);
				}
			}
			yield return 0;
			bool b = false;
			do
			{
				b = false;
				foreach (KeyValuePair<int, Simulation.ServerTimeSample> keyValuePair in BoltLinker.samples)
				{
					if (keyValuePair.Value.WaitingResult)
					{
						b = true;
						break;
					}
				}
				yield return 0;
			}
			while (b);
			List<double> list = new List<double>();
			foreach (KeyValuePair<int, Simulation.ServerTimeSample> keyValuePair2 in BoltLinker.samples)
			{
				if (keyValuePair2.Value.ReceivedResult)
				{
					list.Add(keyValuePair2.Value.GetSample());
				}
			}
			BoltLinker.samples.Clear();
			if (list.Count == 0)
			{
				Debug.LogError("Unable to receive any times");
				BoltNetwork.Shutdown();
				yield break;
			}
			List<double> list2 = new List<double>();
			for (int i = 1; i < list.Count - 1; i++)
			{
				list2.Add((list[i - 1] + list[i] + list[i + 1]) / 3.0);
			}
			double num = 0.0;
			for (int j = 0; j < list2.Count; j++)
			{
				num += list2[j];
			}
			num /= (double)list2.Count;
			RCGRollback.SetServerTime(num);
			yield break;
		}

		// Token: 0x06002EFA RID: 12026 RVA: 0x0001ED9F File Offset: 0x0001CF9F
		public override void OnEvent(TimeSync_Request evnt)
		{
			TimeSync_Result.Post(evnt.RaisedBy, ReliabilityModes.ReliableOrdered, evnt.ID, new BoltLinker.ServerTime(RCGRollback.MainSimulation.ElapsedTimeMiliseconds));
		}

		// Token: 0x06002EFB RID: 12027 RVA: 0x000D4420 File Offset: 0x000D2620
		public override void OnEvent(TimeSync_Result evnt)
		{
			BoltLinker.ServerTime serverTime = evnt.Time as BoltLinker.ServerTime;
			Simulation.ServerTimeSample serverTimeSample;
			if (BoltLinker.samples.TryGetValue(evnt.ID, out serverTimeSample))
			{
				serverTimeSample.SetTicks(serverTime.m_serverElapsedTimeMilliseconds);
			}
		}

		// Token: 0x06002EFC RID: 12028 RVA: 0x000D445C File Offset: 0x000D265C
		public static void ReceiveGameState(byte[] data)
		{
			BinaryPacket binaryPacket = new BinaryPacket(data);
			int num = binaryPacket.ReadInt32();
			int num2 = binaryPacket.ReadInt32();
			int num3 = binaryPacket.ReadInt32();
			if (num2 != BoltLinker.m_currentRequest.m_stateRequestID)
			{
				return;
			}
			binaryPacket.Position = num3;
			int num4 = binaryPacket.ReadInt32();
			for (int i = 0; i < num4; i++)
			{
				int num5 = binaryPacket.ReadInt32();
				EventEntity eventEntity = RCGRollback.RawDataToEvent(binaryPacket);
				RCGRollback.AddEvent<EventEntity>(num5, eventEntity);
			}
			RCGRollback.PrepareDoubleBuffer(num, data, 12, num3 - 12);
			RCGRollback.BackgroundTimeFrames.CurrentSimulate = (RCGRollback.BackgroundTimeFrames.MaxSimulated = (RCGRollback.BackgroundTimeFrames.MinimumFrame = (RCGRollback.BackgroundTimeFrames.LastChecksumFrame = num)));
			using (SimulationIteration backgroundIterator = RCGRollback.BackgroundIterator)
			{
				ServerStateEntity serverStateEntity = ServerStateEntity.Singleton(backgroundIterator);
				if (serverStateEntity == null)
				{
					Debug.LogError("Bad simulation returned (" + num + ")");
					global::Singleton<ScreenManager>.instance.AddMenu<UI_Loading>(UI_Loading.ToMainMenu(), false);
					return;
				}
				BoltLinker.m_currentRequest.readScene = serverStateEntity.m_serverData.CurrentSceneBuildIndex;
				if (BoltLinker.GetSceneIndex(SceneManager.GetActiveScene().name) != BoltLinker.m_currentRequest.readScene)
				{
					RCGRollback.BackgroundSimulating = false;
				}
			}
			BoltLinker.Instance.m_desyncStateMain = BoltLinker.DesyncState.Simulating;
			BoltLinker.m_currentRequest.m_finished = true;
		}

		// Token: 0x06002EFD RID: 12029 RVA: 0x000D45C4 File Offset: 0x000D27C4
		public static void ReceiveMetaState(byte[] data)
		{
			BinaryPacket binaryPacket = new BinaryPacket(data);
			int num = binaryPacket.ReadInt32();
			int num2 = binaryPacket.ReadInt32();
			int num3 = binaryPacket.ReadInt32();
			if (num2 != BoltLinker.m_metaRequest.m_stateRequestID)
			{
				return;
			}
			binaryPacket.Position = num3;
			int num4 = binaryPacket.ReadInt32();
			for (int i = 0; i < num4; i++)
			{
				int num5 = binaryPacket.ReadInt32();
				EventEntity eventEntity = RCGRollback.RawDataToMetaEvent(binaryPacket);
				RCGRollback.AddMetaEvent<EventEntity>(num5, eventEntity);
			}
			RCGRollback.SetMetaState(num, data, 12, num3 - 12);
			int serverFrame = RCGRollback.MainSimulation.ServerFrame;
			RCGRollback.AdvanceMetaSimulation(num, serverFrame);
			BoltLinker.m_mainMetaTimeFrames.MaxSimulated = serverFrame;
			BoltLinker.m_mainMetaTimeFrames.MinimumFrame = serverFrame;
			BoltLinker.m_mainMetaTimeFrames.CurrentSimulate = serverFrame;
			if (RCGRollback.MainTimeFrames.CurrentSimulate == 0)
			{
				RCGRollback.MainTimeFrames.MaxSimulated = serverFrame;
				RCGRollback.MainTimeFrames.MinimumFrame = serverFrame;
				RCGRollback.MainTimeFrames.CurrentSimulate = serverFrame;
			}
			Debug.LogError("ServerFrame " + serverFrame);
			BoltLinker.AddMetaEvent<SetMetaPlayerDesyncedMeta>(new SetMetaPlayerDesyncedMeta
			{
				m_connectionID = BoltLinker.MyConnectionID,
				m_desynced_meta = false
			}, -1);
			BoltLinker.m_metaRequest.m_finished = true;
		}

		// Token: 0x06002EFE RID: 12030 RVA: 0x000D46E8 File Offset: 0x000D28E8
		private static void SendAddSaveSlotEventOrDisconnectBecauseOnSomeonesBlockList(SimulationIteration iteration, bool writeFrame = false)
		{
			OnlineInfo onlineInfo = new OnlineInfo(OnlineInfo.GetThisConsoleType(), OnlineInfo.GetThisAccountId(), OnlineInfo.GetThisDisplayName());
			IRestrictedList<SaveSlotEntity> restrictedList;
			iteration.GetEntities<SaveSlotEntity>(out restrictedList);
			for (int i = 0; i < restrictedList.RestrictedCount; i++)
			{
				if (OnlineInfo.IsOnlineInfoInList(restrictedList[i].m_data.m_blockList, onlineInfo))
				{
					BoltLinker.Blocked = true;
					foreach (BoltConnection boltConnection in BoltNetwork.Connections)
					{
						if (boltConnection.ConnectionId == BoltLinker.MyConnectionID)
						{
							boltConnection.Disconnect();
							return;
						}
					}
				}
			}
			AddSaveSlotEvent addSaveSlotEvent = new AddSaveSlotEvent();
			addSaveSlotEvent.ConnectionID = BoltLinker.MyConnectionID;
			addSaveSlotEvent.m_slotData = new SaveSlotEntity.SlotData(ProfileManager.Profile.ActiveSlot);
			if (writeFrame)
			{
				BoltLinker.AddEvent<AddSaveSlotEvent>(addSaveSlotEvent, iteration.WriteFrame);
			}
			else
			{
				BoltLinker.AddEvent<AddSaveSlotEvent>(addSaveSlotEvent, -1);
			}
			Debug.LogWarning(string.Concat(new object[]
			{
				"AddSaveSlotEvent: ConnectionID: ",
				addSaveSlotEvent.ConnectionID,
				" accountID: ",
				addSaveSlotEvent.m_slotData.m_onlineInfo.m_accountId
			}));
			Debug.LogError(string.Concat(new object[]
			{
				"Sending at LocalSimulate: ",
				RCGRollback.MainTimeFrames.CurrentSimulate,
				" ServerFrame: ",
				RCGRollback.MainSimulation.ServerFrame
			}));
		}

		// Token: 0x06002EFF RID: 12031 RVA: 0x000D4860 File Offset: 0x000D2A60
		private static void SyncFinished(SimulationIteration lastBufferFrame)
		{
			IRestrictedList<SaveSlotEntity> restrictedList;
			lastBufferFrame.GetEntities<SaveSlotEntity>(out restrictedList);
			bool flag = false;
			int num = 0;
			while (!flag && num < restrictedList.RestrictedCount)
			{
				flag = restrictedList[num].ConnectionID == BoltLinker.MyConnectionID;
				num++;
			}
			if (!flag)
			{
				BoltLinker.SendAddSaveSlotEventOrDisconnectBecauseOnSomeonesBlockList(lastBufferFrame, false);
				List<IHandleInput> list = new List<IHandleInput>();
				global::Singleton<InputManager>.instance.FillActiveInputs(list);
				foreach (IHandleInput handleInput in list)
				{
					BoltLinker.Instance.CheckLocalPlayer(handleInput.PlayerID);
				}
				global::Singleton<InputManager>.instance.OnLocalPlayerAdded += BoltLinker.Instance.CheckLocalPlayer;
				global::Singleton<InputManager>.instance.OnLocalPlayerRemoved += BoltLinker.Instance.RemoveLocalPlayer;
			}
			BoltLinker.Instance.m_desyncStateMain = BoltLinker.DesyncState.Synced;
			BoltLinker.AddMetaEvent<SetMetaPlayerDesyncedMain>(new SetMetaPlayerDesyncedMain
			{
				m_connectionID = BoltLinker.MyConnectionID,
				m_desynced_main = false
			}, -1);
		}

		// Token: 0x06002F00 RID: 12032 RVA: 0x0001EDC3 File Offset: 0x0001CFC3
		public static BoltLinker.SyncRequest BeginStateRequest(int id)
		{
			Debug.Log("Ask for gamestate");
			Sync_Request.Post(GlobalTargets.OnlyServer, ReliabilityModes.ReliableOrdered, id);
			BoltLinker.m_currentRequest = new BoltLinker.SyncRequest(id)
			{
				m_finished = false,
				m_stateRequestID = id
			};
			return BoltLinker.m_currentRequest;
		}

		// Token: 0x06002F01 RID: 12033 RVA: 0x0001EDF6 File Offset: 0x0001CFF6
		public static BoltLinker.SyncRequest BeginMetaStateRequest(int id)
		{
			Debug.LogError("Ask for meta gamestate");
			Meta_Request.Post(GlobalTargets.OnlyServer, ReliabilityModes.ReliableOrdered, id);
			BoltLinker.m_metaRequest = new BoltLinker.SyncRequest(id)
			{
				m_finished = false,
				m_stateRequestID = id
			};
			return BoltLinker.m_metaRequest;
		}

		// Token: 0x06002F02 RID: 12034 RVA: 0x0001EE29 File Offset: 0x0001D029
		public IEnumerator MetaStateDownload()
		{
			BoltLinker.SyncRequest m_request = null;
			for (;;)
			{
				IL_30:
				this.m_desyncStateMeta = BoltLinker.DesyncState.Downloading;
				this.m_metaState++;
				m_request = BoltLinker.BeginMetaStateRequest(this.m_metaState);
				while (!m_request.m_finished)
				{
					if (m_request.m_time > 3f)
					{
						Debug.LogError("Request Timeout");
						goto IL_30;
					}
					yield return 0;
					m_request.m_time += Time.deltaTime;
				}
				int serverFrame = RCGRollback.MainSimulation.ServerFrame;
				RCGRollback.AdvanceMetaSimulation(BoltLinker.m_mainMetaTimeFrames.CurrentSimulate, serverFrame);
				BoltLinker.m_mainMetaTimeFrames.CurrentSimulate = (BoltLinker.m_mainMetaTimeFrames.MaxSimulated = serverFrame);
				bool sentEvent = false;
				bool found = false;
				float wait = 0f;
				while (!found && wait <= 10f)
				{
					IRestrictedList<MetaPlayerState> restrictedList;
					RCGRollback.MetaIteration.GetEntities<MetaPlayerState>(out restrictedList);
					int num = 0;
					while (num < restrictedList.RestrictedCount && !found)
					{
						if (restrictedList[num].m_connectionID == BoltLinker.MyConnectionID)
						{
							found = true;
						}
						num++;
					}
					if (!found)
					{
						if (!sentEvent)
						{
							sentEvent = true;
							BoltLinker.AddMetaEvent<AddMetaPlayer>(new AddMetaPlayer
							{
								m_connectionID = BoltLinker.MyConnectionID
							}, -2);
							BoltLinker.AddMetaEvent<SetMetaPlayerDesyncedMeta>(new SetMetaPlayerDesyncedMeta
							{
								m_connectionID = BoltLinker.MyConnectionID,
								m_desynced_meta = false
							}, -1);
						}
						yield return 0;
						wait += Time.deltaTime;
					}
				}
				if (found)
				{
					break;
				}
				Debug.LogError("Could not find self after siming for 10 seconds");
			}
			Debug.Log(string.Concat(new object[]
			{
				"Meta caught Up ",
				RCGRollback.MetaIteration.ReadFrame,
				" ",
				RCGRollback.MetaIteration.GetFirst<MetaServerState>().m_level
			}));
			this.m_desyncStateMeta = BoltLinker.DesyncState.Synced;
			yield break;
		}

		// Token: 0x06002F03 RID: 12035 RVA: 0x0001EE38 File Offset: 0x0001D038
		public IEnumerator BeginSync()
		{
			BoltLinker.SyncRequest m_request = null;
			BoltLinker.GetSceneIndex(SceneManager.GetActiveScene().name);
			BoltLinker.StateRequestID++;
			m_request = BoltLinker.BeginStateRequest(BoltLinker.StateRequestID);
			while (!m_request.m_finished)
			{
				if (m_request.m_time > 3f)
				{
					Debug.LogError("Request Timeout");
					BoltLinker.StateRequestID++;
					m_request = BoltLinker.BeginStateRequest(BoltLinker.StateRequestID);
				}
				yield return 0;
				if (!m_request.m_finished)
				{
					m_request.m_time += Time.deltaTime;
				}
			}
			this.m_syncRoutineMain = null;
			yield break;
		}

		// Token: 0x06002F04 RID: 12036 RVA: 0x0001EE47 File Offset: 0x0001D047
		public IEnumerator WaitForMetaAndLoadNewScene(UI_Loading loading)
		{
			bool resynced = false;
			this.DoubleSyncFinished = false;
			for (;;)
			{
				IL_6F:
				Debug.Log("Begin Sync With Host");
				MetaServerState serverState = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
				if (serverState == null)
				{
					yield return 0;
					while (serverState == null)
					{
						yield return this.MetaStateDownload();
						serverState = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
					}
				}
				else
				{
					Debug.Log("Has ServerState");
					int currentScene = BoltLinker.GetSceneIndex(SceneManager.GetActiveScene().name);
					while (serverState.m_level != currentScene)
					{
						AsyncOperation asyncOperation = SceneManager.LoadSceneAsync(serverState.m_level);
						while (!asyncOperation.isDone)
						{
							loading.SetLoadProgression("Loading Scene...", asyncOperation.progress * 0.8f + 0.1f);
							yield return null;
						}
						serverState = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
						currentScene = BoltLinker.GetSceneIndex(SceneManager.GetActiveScene().name);
						if (serverState == null)
						{
							yield return 0;
							while (serverState == null)
							{
								yield return this.MetaStateDownload();
								serverState = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
							}
							goto IL_6F;
						}
						asyncOperation = null;
					}
					Debug.Log(string.Concat(new object[]
					{
						"In The right Scene ",
						serverState.m_level,
						" ",
						BoltLinker.GetSceneIndex(SceneManager.GetActiveScene().name)
					}));
					MetaServerState first = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
					MetaPlayerState host = ((first != null) ? first.Host(RCGRollback.MetaIteration) : null);
					if (host == null)
					{
						yield return 0;
						Debug.Log("Lost Host");
					}
					else
					{
						yield return 0;
						BoltLinker.AddMetaEvent<SetMetaPlayerLoading>(new SetMetaPlayerLoading
						{
							m_connectionID = BoltLinker.MyConnectionID,
							m_loading = true
						}, -1);
						BoltLinker.AddMetaEvent<SetMetaPlayerDesyncedMain>(new SetMetaPlayerDesyncedMain
						{
							m_connectionID = BoltLinker.MyConnectionID,
							m_desynced_main = true
						}, -1);
						Debug.Log("Send Loading True");
						yield return 0;
						while (host.m_loading)
						{
							yield return 0;
							MetaServerState first2 = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
							MetaServerState first3;
							for (host = ((first2 != null) ? first2.Host(RCGRollback.MetaIteration) : null); host == null; host = ((first3 != null) ? first3.Host(RCGRollback.MetaIteration) : null))
							{
								yield return this.MetaStateDownload();
								first3 = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
							}
						}
						Debug.Log("Host Loaded");
						yield return BoltLinker.SynchronizeTime();
						yield return this.BeginSync();
						Debug.Log("Main Sync Finished");
						serverState = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
						if (serverState.m_level != currentScene)
						{
							Debug.Log("Wrong Scene");
						}
						else if (!RCGRollback.BackgroundSimulating)
						{
							Debug.Log("Failed to sync main");
						}
						else
						{
							BoltLinker.m_maxBackgroundSimFrames = 10;
							SimulationStats stats = default(SimulationStats);
							while (RCGRollback.BackgroundSimulating)
							{
								this.AdvanceBackgroundSim(RCGRollback.MainSimulation.ServerFrame, ref stats);
								yield return 0;
							}
							BoltLinker.m_maxBackgroundSimFrames = 3;
							Debug.Log("Updated main sim to goal");
							yield return 0;
							BoltLinker.AddMetaEvent<SetMetaPlayerLoading>(new SetMetaPlayerLoading
							{
								m_connectionID = BoltLinker.MyConnectionID,
								m_loading = false
							}, -1);
							BoltLinker.AddMetaEvent<SetMetaPlayerLoadedScene>(new SetMetaPlayerLoadedScene
							{
								m_connectionID = BoltLinker.MyConnectionID,
								m_sceneHash = currentScene
							}, -1);
							yield return 0;
							Debug.Log("End sync WaitingForMeta");
							MetaPlayerState me = null;
							for (;;)
							{
								yield return 0;
								IRestrictedList<MetaPlayerState> restrictedList;
								RCGRollback.MetaIteration.GetEntities<MetaPlayerState>(out restrictedList);
								for (int i = 0; i < restrictedList.RestrictedCount; i++)
								{
									if (restrictedList[i].m_connectionID == BoltLinker.MyConnectionID)
									{
										me = restrictedList[i];
									}
								}
								if (me != null && (!resynced || me.m_loading))
								{
									break;
								}
								if (me != null && !me.m_desynced_main)
								{
									goto Block_20;
								}
							}
							resynced = true;
							Debug.Log("Meta thinks I'm still loading");
							continue;
							Block_20:
							Debug.Log("End sync Final");
							serverState = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
							host = serverState.Host(RCGRollback.MetaIteration);
							currentScene = BoltLinker.GetSceneIndex(SceneManager.GetActiveScene().name);
							if (serverState.m_level == currentScene && !host.m_loading)
							{
								break;
							}
							Debug.Log("Host is loading or I'm in the wrong level");
						}
					}
				}
			}
			this.DoubleSyncFinished = true;
			Debug.Log("Exiting wait for meta");
			yield break;
		}

		// Token: 0x06002F05 RID: 12037 RVA: 0x0001EE5D File Offset: 0x0001D05D
		public static void MovieStart()
		{
			BoltLinker.MovieHack = true;
		}

		// Token: 0x06002F06 RID: 12038 RVA: 0x0001EE65 File Offset: 0x0001D065
		public static void MovieEnd()
		{
			BoltLinker.MovieHack = false;
		}

		// Token: 0x06002F07 RID: 12039 RVA: 0x000D4968 File Offset: 0x000D2B68
		public override void BoltStartBegin()
		{
			BoltLinker.Instance = this;
			this.ServerDelay = 0;
			BoltLinker.Blocked = false;
			BoltLinker.Kicked = false;
			this.myLevelState = 0U;
			this.m_desyncStateMain = BoltLinker.DesyncState.Downloading;
			this.m_desyncStateMeta = BoltLinker.DesyncState.Downloading;
			BoltLinker.syncFallbackTimer = 5f;
			BoltLinker.PacketID = -1;
			this.CurrentEventChunk = -1;
			BoltNetwork.RegisterTokenClass<HostSessionToken>();
			BoltNetwork.RegisterTokenClass<JoinRequestToken>();
			BoltNetwork.RegisterTokenClass<BoltLinker.ServerTime>();
			BoltNetwork.RegisterTokenClass<ConnectionError>();
			BoltNetwork.RegisterTokenClass<BoltTokenData>();
			RCGRollback.Clear();
			SceneReferenceVersionUpdated.version = -1;
			BoltLinker.m_localControllers.Clear();
			RCGRollback.SetServerTime(0.0);
			BoltLinker.m_mainMetaTimeFrames.Reset();
			RCGRollback.MainSimulation.ServerFrame = 0;
			RCGRollback.BackgroundSimulation.ServerFrame = 0;
			global::Singleton<InputManager>.instance.SetControllerLimit(this.ControllerLimit());
			if (BoltNetwork.IsServer)
			{
				this.StartServer();
				global::Singleton<InputManager>.instance.OnLocalPlayerAdded += this.CheckLocalPlayer;
				global::Singleton<InputManager>.instance.OnLocalPlayerRemoved += this.RemoveLocalPlayer;
			}
			BoltLinker.isReturningFromSleep = false;
			if (BoltNetwork.IsSinglePlayer)
			{
				global::Singleton<InputManager>.instance.OnSinglePlayerControllerDisconnected += this.OnSinglePlayerControllerDisconnected;
				return;
			}
			UserProfileManager.Instance.StartOnlinePlay(new OnlinePermissionLostCallback(this.LostPermissionsCallback));
		}

		// Token: 0x06002F08 RID: 12040 RVA: 0x0001EE6D File Offset: 0x0001D06D
		public void StartOnlinePlay()
		{
			base.StartCoroutine(this.DelayedStartOnlinePlay());
		}

		// Token: 0x06002F09 RID: 12041 RVA: 0x0001EE7C File Offset: 0x0001D07C
		private IEnumerator DelayedStartOnlinePlay()
		{
			yield return new WaitForEndOfFrame();
			while (BoltLinker.MovieHack)
			{
				yield return new WaitForEndOfFrame();
			}
			UserProfileManager.Instance.StartOnlinePlay(new OnlinePermissionLostCallback(this.LostPermissionsCallback));
			yield break;
		}

		// Token: 0x06002F0A RID: 12042 RVA: 0x000D4A9C File Offset: 0x000D2C9C
		private void LostPermissionsCallback()
		{
			Debug.LogError("LostPermissionsCallback. Disconnecting");
			foreach (BoltConnection boltConnection in BoltNetwork.Connections)
			{
				if (boltConnection.ConnectionId == BoltLinker.MyConnectionID)
				{
					boltConnection.Disconnect();
					break;
				}
			}
		}

		// Token: 0x06002F0B RID: 12043 RVA: 0x000D4B00 File Offset: 0x000D2D00
		private void OnSinglePlayerControllerDisconnected()
		{
			if (BoltLinker.m_localControllers.Count > 0)
			{
				for (int i = 0; i < BoltLinker.m_localControllers.Count; i++)
				{
					if (BoltLinker.m_localControllers[i].LocalPlayerID == global::Singleton<InputManager>.instance.GetLastPlayerConnected())
					{
						Debug.Log("BoltLinker opening Cellphone for last player connected " + global::Singleton<InputManager>.instance.GetLastPlayerConnected());
						BoltLinker.AddEvent<SetCellphoneMenuOpen>(new SetCellphoneMenuOpen
						{
							m_controllerID = BoltLinker.m_localControllers[i].m_controllerSimualtionID,
							m_forceIntoState = true
						}, -1);
						return;
					}
				}
				BoltLinker.AddEvent<SetCellphoneMenuOpen>(new SetCellphoneMenuOpen
				{
					m_controllerID = BoltLinker.m_localControllers[0].m_controllerSimualtionID,
					m_forceIntoState = true
				}, -1);
				Debug.Log("BoltLinker opening Cellphone for player " + BoltLinker.m_localControllers[0].m_controllerSimualtionID);
			}
		}

		// Token: 0x06002F0C RID: 12044 RVA: 0x000D4BE4 File Offset: 0x000D2DE4
		private void StartServer()
		{
			RCGRollback.MetaIteration.Clear();
			BoltLinker.m_mainMetaTimeFrames.Reset();
			BoltLinker.AddMetaEvent<AddMetaPlayerAsServer>(new AddMetaPlayerAsServer
			{
				m_connectionID = BoltLinker.MyConnectionID
			}, -1);
			using (SimulationIteration simulationIteration = RCGRollback.MigrateAndOpenFrame(0, 0))
			{
				SaveSlot activeSlot = ProfileManager.Profile.ActiveSlot;
				simulationIteration.CreateEntity<EntityCacheCatalog>();
				ServerStateEntity serverStateEntity = simulationIteration.CreateEntity<ServerStateEntity>();
				serverStateEntity.m_serverData.Seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
				serverStateEntity.m_time.m_currentDayNightInSeconds = activeSlot.m_CurrentDayNightTimeInSeconds;
				serverStateEntity.m_time.m_day = activeSlot.m_day;
				serverStateEntity.VoLanguage = ProfileManager.Profile.ProfileSettings.VoiceOverLanguage;
				activeSlot.LoadIntoSimulation(serverStateEntity.m_serverData);
				if (BoltNetwork.IsSinglePlayer)
				{
					simulationIteration.CreateEntity<SinglePlayerBoundsEntity>();
				}
				serverStateEntity.m_serverData.CurrentSceneBuildIndex = BoltLinker.GetSceneIndex(SceneManager.GetActiveScene().name);
				SaveSlotEntity.Create(simulationIteration, BoltLinker.MyConnectionID, serverStateEntity.m_serverData.CurrentSceneBuildIndex, new SaveSlotEntity.SlotData(activeSlot));
				simulationIteration.CreateEntity<BackgroundMusicEntity>();
				simulationIteration.CreateEntity<ComboCounterEntity>();
				simulationIteration.CreateEntity<VendingMachineRespawnManager>();
				List<IHandleInput> list = new List<IHandleInput>();
				global::Singleton<InputManager>.instance.FillActiveInputs(list);
				foreach (IHandleInput handleInput in list)
				{
					BoltLinker.LocalControllerData localControllerData = new BoltLinker.LocalControllerData
					{
						LocalPlayerID = handleInput.PlayerID
					};
					BoltLinker.m_localControllers.Add(localControllerData);
					PlayerControllerEntity.Create(BoltLinker.MyConnectionID, (byte)handleInput.PlayerID, simulationIteration);
				}
				simulationIteration.Write(RCGRollback.MainSimulation[0]);
			}
			RCGRollback.SetServerTime();
		}

		// Token: 0x06002F0D RID: 12045 RVA: 0x000D4DC0 File Offset: 0x000D2FC0
		private void CheckLocalPlayer(int LocalPlayerID)
		{
			bool flag = false;
			int num = 0;
			while (!flag && num < BoltLinker.m_localControllers.Count)
			{
				flag = BoltLinker.m_localControllers[num].LocalPlayerID == LocalPlayerID;
				num++;
			}
			if (!flag && global::Singleton<InputManager>.instance.HasActiveInputHandle(LocalPlayerID))
			{
				BoltLinker.LocalControllerData localControllerData = new BoltLinker.LocalControllerData
				{
					LocalPlayerID = LocalPlayerID
				};
				BoltLinker.m_localControllers.Add(localControllerData);
			}
		}

		// Token: 0x06002F0E RID: 12046 RVA: 0x000D4E24 File Offset: 0x000D3024
		private void RemoveLocalPlayer(int LocalPlayerID)
		{
			for (int i = 0; i < BoltLinker.m_localControllers.Count; i++)
			{
				if (BoltLinker.m_localControllers[i].LocalPlayerID == LocalPlayerID)
				{
					BoltLinker.AddEvent<DeletePlayerEvent>(new DeletePlayerEvent(BoltLinker.MyConnectionID, (byte)LocalPlayerID), -1);
					BoltLinker.m_localControllers.RemoveAt(i);
					return;
				}
			}
		}

		// Token: 0x06002F0F RID: 12047 RVA: 0x000D4E78 File Offset: 0x000D3078
		public static InputMasks GetControllerInput(int LocalPlayerID)
		{
			for (int i = 0; i < BoltLinker.m_localControllers.Count; i++)
			{
				if (BoltLinker.m_localControllers[i].LocalPlayerID == LocalPlayerID)
				{
					return BoltLinker.m_localControllers[i].m_currentInput;
				}
			}
			return default(InputMasks);
		}

		// Token: 0x06002F10 RID: 12048 RVA: 0x000D4EC8 File Offset: 0x000D30C8
		public static void SetControllerInput(int LocalPlayerID, InputMasks mask)
		{
			for (int i = 0; i < BoltLinker.m_localControllers.Count; i++)
			{
				if (BoltLinker.m_localControllers[i].LocalPlayerID == LocalPlayerID)
				{
					BoltLinker.LocalControllerData localControllerData = BoltLinker.m_localControllers[i];
					localControllerData.m_currentInput = mask;
					BoltLinker.m_localControllers[i] = localControllerData;
				}
			}
		}

		// Token: 0x06002F11 RID: 12049 RVA: 0x0001EE8B File Offset: 0x0001D08B
		public static SimulationIteration OpenCurrentFrame()
		{
			return RCGRollback.MainIterator;
		}

		// Token: 0x06002F12 RID: 12050 RVA: 0x0001EE8B File Offset: 0x0001D08B
		public static SimulationIteration OpenMaxSimulated()
		{
			return RCGRollback.MainIterator;
		}

		// Token: 0x06002F13 RID: 12051 RVA: 0x000D4F1C File Offset: 0x000D311C
		public static void LoadCachedState(BoltLinker.CacheEnum Name, out int targetScene, out bool ReloadServer)
		{
			targetScene = -1;
			ReloadServer = false;
			if (!BoltNetwork.IsServer)
			{
				return;
			}
			bool flag = Name == BoltLinker.CacheEnum.Hideout;
			if (flag)
			{
				Name = BoltLinker.CacheEnum.Scene;
			}
			SimulationFrame simulationFrame;
			if (!BoltLinker.m_serverCachedStates.TryGetValue(Name, out simulationFrame))
			{
				Debug.LogError("Unknown Game State! " + Name);
				return;
			}
			int serverFrame = RCGRollback.MainSimulation.ServerFrame;
			BoltLinker.Instance.InputFrame = serverFrame;
			using (SimulationIteration simulationIteration = RCGRollback.MigrateAndOpenFrame(simulationFrame, serverFrame))
			{
				ServerStateEntity sse = ServerStateEntity.Singleton(simulationIteration);
				ServerStateEntity sse2 = sse;
				sse2.cacheVersion += 1;
				targetScene = sse.m_serverData.CurrentSceneBuildIndex;
				int currentSceneBuildIndex = sse.m_serverData.CurrentSceneBuildIndex;
				int lastHideoutBuildIndex = sse.m_serverData.LastHideoutBuildIndex;
				List<IHandleInput> list = new List<IHandleInput>();
				global::Singleton<InputManager>.instance.FillActiveInputs(list);
				foreach (IHandleInput handleInput in list)
				{
					BoltLinker.Instance.CheckLocalPlayer(handleInput.PlayerID);
				}
				if (flag)
				{
					AsyncOperationHandle<QuestEventChainReset> asyncOperationHandle = Addressables.LoadAssetAsync<QuestEventChainReset>("Events/QuestChainResetData");
					asyncOperationHandle.Completed += delegate(AsyncOperationHandle<QuestEventChainReset> handle)
					{
						handle.Result.ProcessQuestChainReset(sse);
					};
					asyncOperationHandle.WaitForCompletion();
					sse.m_serverData.CurrentSceneBuildIndex = sse.m_serverData.LastHideoutBuildIndex;
					sse.m_serverData.LastSceneBuildIndex = sse.m_serverData.CurrentSceneBuildIndex;
					sse.m_serverData.ToDoorHash = 0;
				}
				sse.m_serverData.SetDoorCountDown(-1f);
				PlayerDeathToll.RunPlayerDeathToll_FromBoltLinker(flag, simulationIteration, Name);
				sse.VoLanguage = ProfileManager.Profile.ProfileSettings.VoiceOverLanguage;
				if (!flag)
				{
					simulationIteration.CreateEntity<LoadFromCacheEntity>();
				}
				simulationIteration.Write(RCGRollback.MainSimulation[simulationIteration.WriteFrame]);
			}
			foreach (ScreenManager.MenuScope menuScope in global::Singleton<ScreenManager>.instance)
			{
				foreach (UIScreen uiscreen in menuScope)
				{
					uiscreen.OnSceneLoaded();
				}
			}
			CameraSystem.active.CurrentCamera.Snap();
		}

		// Token: 0x06002F14 RID: 12052 RVA: 0x000D51E8 File Offset: 0x000D33E8
		public static void SetCachedState(BoltLinker.CacheEnum Name, SimulationIteration iteration)
		{
			SimulationFrame simulationFrame;
			if (!BoltLinker.m_serverCachedStates.TryGetValue(Name, out simulationFrame))
			{
				simulationFrame = new SimulationFrame(65536);
				BoltLinker.m_serverCachedStates.Add(Name, simulationFrame);
			}
			iteration.WriteToFrame(simulationFrame);
		}

		// Token: 0x06002F15 RID: 12053 RVA: 0x000D5224 File Offset: 0x000D3424
		public static void AddEvent<T>(T Event, int frame = -1) where T : EventEntity
		{
			if (frame == -1)
			{
				frame = Mathf.Max(RCGRollback.MainSimulation.ServerFrame, RCGRollback.MainTimeFrames.MaxSimulated);
			}
			Event.NetType = RCGRollback.NetIDOf<T>();
			BoltLinker.SendEventThroughNetwork(Event, frame, BoltLinker.PacketType.Event);
			RCGRollback.AddEvent<T>(frame, Event);
			RCGRollback.MainTimeFrames.CurrentSimulate = Mathf.Min(frame, RCGRollback.MainTimeFrames.CurrentSimulate);
		}

		// Token: 0x06002F16 RID: 12054 RVA: 0x000D5290 File Offset: 0x000D3490
		private static void SendEventThroughNetwork(EventEntity Event, int frame, BoltLinker.PacketType type)
		{
			if (BoltNetwork.IsSinglePlayer)
			{
				return;
			}
			BoltLinker.evtPacket.Source = BoltLinker.evtWriteBuffer;
			BoltLinker.evtPacket.WriteInt32(frame);
			BoltLinker.evtPacket.WriteUInt32(BoltLinker.MyConnectionID);
			Event.Serialize(BoltLinker.evtPacket);
			BoltLinker.SendRawDataThroughNetwork(BoltLinker.evtWriteBuffer, BoltLinker.evtPacket.Position, type, null);
		}

		// Token: 0x06002F17 RID: 12055 RVA: 0x000D52F0 File Offset: 0x000D34F0
		private static void SendRawDataThroughNetwork(byte[] data, int length, BoltLinker.PacketType type, BoltConnection conn = null)
		{
			int num = length / 1024 + 1;
			if (BoltLinker.PacketID == 2147483647)
			{
				BoltLinker.PacketID = -1;
			}
			BoltLinker.PacketID++;
			for (int i = 0; i < num; i++)
			{
				BoltTokenData token = ProtocolTokenUtils.GetToken<BoltTokenData>();
				int num2 = Mathf.Min(1024, length - i * 1024);
				Array.Copy(data, i * 1024, token.Data, 0, num2);
				if (conn != null)
				{
					EventChunk.Post(conn, ReliabilityModes.ReliableOrdered, BoltLinker.PacketID, i + 1, num, token, (int)type);
				}
				else
				{
					EventChunk.Post(GlobalTargets.Others, ReliabilityModes.ReliableOrdered, BoltLinker.PacketID, i + 1, num, token, (int)type);
				}
			}
		}

		// Token: 0x06002F18 RID: 12056 RVA: 0x000D538C File Offset: 0x000D358C
		public override void OnEvent(EventChunk evnt)
		{
			if (this.CurrentEventChunk != evnt.PacketID)
			{
				this.CurrentEventChunk = evnt.PacketID;
				BoltLinker.pos = 0;
			}
			byte[] data = (evnt.PacketData as BoltTokenData).Data;
			Array.Copy(data, 0, BoltLinker.incoming, BoltLinker.pos, data.Length);
			BoltLinker.pos += data.Length;
			if (evnt.PacketNum == evnt.PacketTotalSize)
			{
				switch (evnt.IsMetaEvent)
				{
				case 0:
					BoltLinker.ReceivedEventStreamed(BoltLinker.incoming);
					break;
				case 1:
					BoltLinker.ReceivedMetaEventStreamed(BoltLinker.incoming);
					break;
				case 2:
					BoltLinker.ReceiveMetaState(BoltLinker.incoming);
					break;
				case 3:
					BoltLinker.ReceiveGameState(BoltLinker.incoming);
					break;
				}
				BoltLinker.pos = 0;
			}
		}

		// Token: 0x06002F19 RID: 12057 RVA: 0x0001EE92 File Offset: 0x0001D092
		public override void OnEvent(SimEvt evnt)
		{
			BoltLinker.ReceivedEventStreamed((evnt.Data as BoltTokenData).Data);
		}

		// Token: 0x06002F1A RID: 12058 RVA: 0x000D5450 File Offset: 0x000D3650
		public static void AddMetaEvent<T>(T Event, int frame = -1) where T : EventEntity
		{
			int serverFrame = RCGRollback.MainSimulation.ServerFrame;
			RCGRollback.AdvanceMetaSimulation(BoltLinker.m_mainMetaTimeFrames.CurrentSimulate, serverFrame);
			BoltLinker.m_mainMetaTimeFrames.CurrentSimulate = (BoltLinker.m_mainMetaTimeFrames.MaxSimulated = serverFrame);
			if (frame < 0)
			{
				frame = Mathf.Max(0, serverFrame + frame);
			}
			Event.NetType = RCGRollback.MetaNetIDOf<T>();
			EntityLog entityLog = new EntityLog
			{
				m_counter = new EntityLog.Counter()
			};
			Event.FillLog(entityLog);
			Debug.LogError(string.Concat(new object[] { "Sending Meta at frame ", frame, "\n", entityLog }));
			BoltLinker.SendEventThroughNetwork(Event, frame, BoltLinker.PacketType.MetaEvent);
			RCGRollback.AddMetaEvent<T>(frame, Event);
			BoltLinker.m_mainMetaTimeFrames.CurrentSimulate = Mathf.Min(frame, BoltLinker.m_mainMetaTimeFrames.CurrentSimulate);
		}

		// Token: 0x06002F1B RID: 12059 RVA: 0x0001EEA9 File Offset: 0x0001D0A9
		public override void OnEvent(MetaSimEvt evnt)
		{
			BoltLinker.ReceivedMetaEventStreamed((evnt.Data as BoltTokenData).Data);
		}

		// Token: 0x06002F1C RID: 12060 RVA: 0x000D5528 File Offset: 0x000D3728
		public static void ReceivedEventStreamed(byte[] evtArr)
		{
			BoltLinker.evtPacket.Source = evtArr;
			int num = BoltLinker.evtPacket.ReadInt32();
			BoltLinker.evtPacket.ReadUInt32();
			EventEntity eventEntity = RCGRollback.RawDataToEvent(BoltLinker.evtPacket);
			int num2 = Mathf.Min(num, RCGRollback.MainTimeFrames.MaxSimulated);
			int num3 = RCGRollback.MainTimeFrames.MaxSimulated - num2;
			if (num3 >= 30)
			{
				Debug.LogError(string.Concat(new object[]
				{
					"Event to far in past or future ",
					num3,
					" ",
					num2,
					" ",
					num,
					" ",
					eventEntity.GetType()
				}));
			}
			else
			{
				RCGRollback.AddEvent<EventEntity>(num, eventEntity);
				RCGRollback.MainTimeFrames.CurrentSimulate = Mathf.Min(num, RCGRollback.MainTimeFrames.CurrentSimulate);
			}
			BoltLinker.Instance.RollbackFrames.Add(num3);
			if (BoltLinker.Instance.RollbackFrames.Count > 10)
			{
				BoltLinker.Instance.RollbackFrames.RemoveAt(0);
			}
		}

		// Token: 0x06002F1D RID: 12061 RVA: 0x000D5630 File Offset: 0x000D3830
		public static void ReceivedMetaEventStreamed(byte[] evtArr)
		{
			BoltLinker.evtPacket.Source = evtArr;
			int num = BoltLinker.evtPacket.ReadInt32();
			uint num2 = BoltLinker.evtPacket.ReadUInt32();
			EventEntity eventEntity = RCGRollback.RawDataToMetaEvent(BoltLinker.evtPacket);
			int serverFrame = RCGRollback.MainSimulation.ServerFrame;
			RCGRollback.AdvanceMetaSimulation(BoltLinker.m_mainMetaTimeFrames.CurrentSimulate, serverFrame);
			BoltLinker.m_mainMetaTimeFrames.CurrentSimulate = (BoltLinker.m_mainMetaTimeFrames.MaxSimulated = serverFrame);
			int num3 = Mathf.Min(num, BoltLinker.m_mainMetaTimeFrames.MaxSimulated);
			int num4 = BoltLinker.m_mainMetaTimeFrames.MaxSimulated - num3;
			EntityLog entityLog = new EntityLog
			{
				m_counter = new EntityLog.Counter()
			};
			eventEntity.FillLog(entityLog);
			Debug.LogError(string.Concat(new object[] { "Recieved Meta at frame ", num, "\n", entityLog }));
			if (num4 >= 100)
			{
				Debug.LogError(string.Concat(new object[]
				{
					"Event to far in past or future ",
					num4,
					" ",
					num3,
					" ",
					num,
					" ",
					BoltLinker.m_mainMetaTimeFrames.MaxSimulated,
					" ",
					eventEntity.GetType(),
					" ",
					num2
				}));
				return;
			}
			if (num < BoltLinker.m_mainMetaTimeFrames.MinimumFrame)
			{
				Debug.LogError(string.Format("frame {0} is less than my minimum frame! {1}", num, BoltLinker.m_mainMetaTimeFrames));
				return;
			}
			RCGRollback.AddMetaEvent<EventEntity>(num, eventEntity);
			BoltLinker.m_mainMetaTimeFrames.CurrentSimulate = Mathf.Min(num, BoltLinker.m_mainMetaTimeFrames.CurrentSimulate);
		}

		// Token: 0x06002F1E RID: 12062 RVA: 0x000D57E0 File Offset: 0x000D39E0
		public void SetServerStateRender(SimulationIteration iteration, ServerStateEntity ent)
		{
			iteration.GetEntities<SaveSlotEntity>(out BoltLinker.slots);
			SaveSlotEntity saveSlotEntity = null;
			for (int i = 0; i < BoltLinker.slots.RestrictedCount; i++)
			{
				if (BoltLinker.slots[i].ConnectionID == BoltLinker.MyConnectionID)
				{
					saveSlotEntity = BoltLinker.slots[i];
				}
			}
			if (saveSlotEntity == null)
			{
				BoltLinker.SendAddSaveSlotEventOrDisconnectBecauseOnSomeonesBlockList(iteration, true);
			}
			else
			{
				List<PlayerControllerEntity> list = saveSlotEntity.ReadOnlyControllers(iteration);
				for (int j = 0; j < BoltLinker.m_localControllers.Count; j++)
				{
					bool flag = false;
					for (int k = 0; k < list.Count; k++)
					{
						if (BoltLinker.m_localControllers[j].LocalPlayerID == list[k].LocalID)
						{
							flag = true;
						}
					}
					if (!flag)
					{
						BoltLinker.AddEvent<SpawnPlayerEvent>(new SpawnPlayerEvent
						{
							m_ConnectionID = BoltLinker.MyConnectionID,
							m_localID = (byte)BoltLinker.m_localControllers[j].LocalPlayerID
						}, iteration.WriteFrame);
					}
				}
				List<PlayerControllerEntity> list2 = saveSlotEntity.ReadOnlyControllers(iteration);
				for (int l = 0; l < list2.Count; l++)
				{
					bool flag2 = false;
					for (int m = 0; m < BoltLinker.m_localControllers.Count; m++)
					{
						if (BoltLinker.m_localControllers[m].LocalPlayerID == list2[l].LocalID)
						{
							flag2 = true;
						}
					}
					if (!flag2)
					{
						BoltLinker.AddEvent<DeletePlayerEvent>(new DeletePlayerEvent
						{
							m_connectionID = BoltLinker.MyConnectionID,
							m_localPlayerID = (byte)list2[l].LocalID
						}, iteration.WriteFrame);
					}
				}
				bool flag3 = false;
				if (BoltNetwork.IsSinglePlayer)
				{
					flag3 = global::Singleton<InputManager>.instance.HasUnactiveController;
				}
				if (saveSlotEntity != null && flag3 != saveSlotEntity.controllerAvailable)
				{
					BoltLinker.AddEvent<SetControllerAvailable>(new SetControllerAvailable(BoltLinker.MyConnectionID, flag3), -1);
				}
			}
			LoadingScreenEntity first = iteration.GetFirst<LoadingScreenEntity>();
			BoltLinker.lookingforLoad = first != null && first.LookingForGameStart;
			if (BoltNetwork.IsServer)
			{
				bool flag4 = true;
				for (int n = 0; n < this.m_metaPlayers.RestrictedCount; n++)
				{
					if (this.m_metaPlayers[n].m_connectionID == BoltLinker.MyConnectionID)
					{
						this.m_metaPlayer = this.m_metaPlayers[n];
					}
					else
					{
						bool flag5 = false;
						using (IEnumerator<BoltConnection> enumerator = BoltNetwork.Connections.GetEnumerator())
						{
							while (enumerator.MoveNext())
							{
								if (enumerator.Current.ConnectionId == this.m_metaPlayers[n].m_connectionID)
								{
									flag5 = true;
								}
							}
						}
						if (!flag5)
						{
							BoltLinker.AddEvent<DeleteSaveSlotEvent>(new DeleteSaveSlotEvent
							{
								ConnectionID = this.m_metaPlayers[n].m_connectionID
							}, -1);
							BoltLinker.AddMetaEvent<DeleteMetaPlayer>(new DeleteMetaPlayer
							{
								m_connectionID = this.m_metaPlayers[n].m_connectionID
							}, -1);
						}
						else
						{
							flag4 &= this.m_metaPlayers[n].Ready;
						}
					}
				}
				if (BoltLinker.lookingforLoad && flag4)
				{
					BoltLinker.AddEvent<StartGameEvent>(new StartGameEvent(), -1);
				}
				ServerStateEntity serverStateEntity = ServerStateEntity.Singleton(iteration);
				MetaServerState first2 = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
				if (serverStateEntity.m_serverData.CurrentSceneBuildIndex != first2.m_level)
				{
					Debug.LogError(serverStateEntity.m_serverData.CurrentSceneBuildIndex + " " + first2.m_level);
					BoltLinker.AddMetaEvent<SetMetaServerLevelState>(new SetMetaServerLevelState
					{
						m_levelState = first2.m_levelState + 1U
					}, -1);
					BoltLinker.AddMetaEvent<SetMetaServerStateLevelData>(new SetMetaServerStateLevelData
					{
						m_level = serverStateEntity.m_serverData.CurrentSceneBuildIndex,
						m_fromLevel = serverStateEntity.m_serverData.LastSceneBuildIndex,
						m_toDoor = serverStateEntity.m_serverData.ToDoorHash
					}, -1);
					return;
				}
				if (BoltLinker.m_reloadFromCache != BoltLinker.CacheEnum.None)
				{
					int num;
					bool flag6;
					BoltLinker.LoadCachedState(BoltLinker.m_reloadFromCache, out num, out flag6);
					IRestrictedList<WeaponEntity> restrictedList;
					iteration.GetEntities<WeaponEntity>(out restrictedList);
					foreach (WeaponEntity weaponEntity in restrictedList)
					{
						if (weaponEntity.StateMachine.GetCurrentStateAs<WeaponEntity.WeaponHeld>() != null)
						{
							iteration.Destroy(weaponEntity);
						}
					}
					if (!BoltNetwork.IsSinglePlayer && BoltNetwork.IsServer)
					{
						iteration.GetEntities<PlayerControllerEntity>(out BoltLinker.pces);
						if (BoltNetwork.Connections.Count<BoltConnection>() <= 0)
						{
							foreach (PlayerControllerEntity playerControllerEntity in BoltLinker.pces)
							{
								if (!playerControllerEntity.IsLocalPlayer(iteration))
								{
									iteration.Destroy(playerControllerEntity);
									iteration.Destroy(playerControllerEntity.SaveSlot(iteration));
								}
							}
						}
					}
					Debug.LogError("Cache reload");
					serverStateEntity = ServerStateEntity.Singleton(RCGRollback.MainIterator);
					BoltLinker.AddMetaEvent<SetMetaServerLevelState>(new SetMetaServerLevelState
					{
						m_levelState = first2.m_levelState + 1U
					}, -1);
					BoltLinker.AddMetaEvent<SetMetaServerStateLevelData>(new SetMetaServerStateLevelData
					{
						m_level = serverStateEntity.m_serverData.CurrentSceneBuildIndex,
						m_fromLevel = serverStateEntity.m_serverData.LastSceneBuildIndex,
						m_toDoor = serverStateEntity.m_serverData.ToDoorHash
					}, -1);
					BoltLinker.m_reloadFromCache = BoltLinker.CacheEnum.None;
				}
			}
		}

		// Token: 0x06002F1F RID: 12063 RVA: 0x0001EEC0 File Offset: 0x0001D0C0
		public static void SetServerState(SimulationIteration iteration, ServerStateEntity ent)
		{
			ForceReloadEntity first = iteration.GetFirst<ForceReloadEntity>();
			BoltLinker.m_reloadFromCache = ((first != null) ? first.m_reloadType : BoltLinker.CacheEnum.None);
		}

		// Token: 0x06002F20 RID: 12064 RVA: 0x000022A1 File Offset: 0x000004A1
		private void OnGUI()
		{
		}

		// Token: 0x06002F21 RID: 12065 RVA: 0x0001BB17 File Offset: 0x00019D17
		private int ControllerLimit()
		{
			return 4;
		}

		// Token: 0x06002F22 RID: 12066 RVA: 0x000D5D00 File Offset: 0x000D3F00
		public void Update()
		{
			if (BoltNetwork.IsSinglePlayer && !Debugger.SimulateRollbackInSinglePlayer)
			{
				this.SinglePlayerTime += Time.deltaTime;
				if (this.SinglePlayerTime > RCGRollback.System.FPSTime)
				{
					Simulation mainSimulation = RCGRollback.MainSimulation;
					int serverFrame = mainSimulation.ServerFrame;
					mainSimulation.ServerFrame = serverFrame + 1;
					this.SinglePlayerTime %= RCGRollback.System.FPSTime;
				}
				RCGRollback.MainSimulation.InterpolationFactor = this.SinglePlayerTime / RCGRollback.System.FPSTime;
			}
			int serverFrame2 = RCGRollback.MainSimulation.ServerFrame;
			float interpolationFactor = RCGRollback.MainSimulation.InterpolationFactor;
			this.UpdateMetaState(serverFrame2);
			if (Input.GetKeyDown(KeyCode.PageUp))
			{
				Debug.Log("Server Frame: " + RCGRollback.MainSimulation.ServerFrame);
			}
			if (serverFrame2 - RCGRollback.MainTimeFrames.CurrentSimulate > 1800)
			{
				if (this.m_desyncStateMain != BoltLinker.DesyncState.Synced)
				{
					RCGRollback.BackgroundSimulating = false;
					if (this.m_syncRoutineMain != null)
					{
						base.StopCoroutine(this.m_syncRoutineMain);
					}
				}
				Debug.Log("Migrating Main Frame Due to Time diff");
				RCGRollback.MainIterator.ReadFrame = (RCGRollback.MainIterator.WriteFrame = serverFrame2);
				RCGRollback.MainIterator.Write(RCGRollback.MainSimulation[serverFrame2]);
				RCGRollback.MainTimeFrames.SetAll(serverFrame2);
			}
			this.frameCount++;
			this.dt += Time.deltaTime;
			if (this.dt > 1f / this.updateRate)
			{
				this.fps = (float)this.frameCount / this.dt;
				this.frameCount = 0;
				this.dt -= 1f / this.updateRate;
			}
			if (BoltNetwork.IsServer)
			{
				if (BoltLinker.pingUpdate >= 2)
				{
					foreach (BoltConnection boltConnection in BoltNetwork.Connections)
					{
						BoltLinker.pings[boltConnection].Add(boltConnection.PingAliased);
					}
					BoltLinker.pingUpdate = 0;
				}
				else
				{
					BoltLinker.pingUpdate++;
				}
			}
			this.m_metaPlayer = null;
			RCGRollback.MetaIteration.GetEntities<MetaPlayerState>(out this.m_metaPlayers);
			for (int i = 0; i < this.m_metaPlayers.RestrictedCount; i++)
			{
				if (this.m_metaPlayers[i].m_connectionID == BoltLinker.MyConnectionID)
				{
					this.m_metaPlayer = this.m_metaPlayers[i];
				}
			}
			if (this.m_metaPlayer == null)
			{
				return;
			}
			SimulationStats simulationStats = default(SimulationStats);
			RCGRollback.System.Enabled = !this.m_metaPlayer.m_loading;
			if (!this.m_metaPlayer.m_loading)
			{
				this.HandleInput(serverFrame2);
				if (this.m_metaPlayer.m_desynced_main && RCGRollback.BackgroundSimulating)
				{
					this.AdvanceBackgroundSim(serverFrame2, ref simulationStats);
				}
				RCGRollback.AdvanceMainSimulation(RCGRollback.MainTimeFrames.CurrentSimulate, serverFrame2, interpolationFactor, ref simulationStats);
				RCGRollback.MainTimeFrames.CurrentSimulate = serverFrame2;
				RCGRollback.MainTimeFrames.MaxSimulated = serverFrame2;
				if (Input.GetKeyDown(KeyCode.P))
				{
					EntityLog entityLog = new EntityLog
					{
						m_counter = new EntityLog.Counter(),
						m_data = "Client send"
					};
					SimulationIteration simulationIteration = new SimulationIteration(RCGRollback.System, 65536);
					simulationIteration.Read(RCGRollback.MainSimulation[RCGRollback.MainTimeFrames.CurrentSimulate]);
					IRestrictedList<IOnPrepareFrame> restrictedList;
					simulationIteration.GetEntities<IOnPrepareFrame>(out restrictedList);
					for (int j = 0; j < restrictedList.RestrictedCount; j++)
					{
						restrictedList[j].OnPrepareFrame(simulationIteration);
					}
					simulationIteration.FillLog(entityLog);
					Debug.LogWarning("Current Frame Log\n" + entityLog);
				}
				RCGRollback.MainTimeFrames.MinimumFrame = Mathf.Max(RCGRollback.MainTimeFrames.MinimumFrame, RCGRollback.MainTimeFrames.MaxSimulated - 119);
				if (!this.m_metaPlayer.m_desynced_main && !BoltNetwork.IsServer && RCGRollback.MainTimeFrames.LastChecksumFrame + BoltLinker.ChecksumPeriod < RCGRollback.MainTimeFrames.MaxSimulated)
				{
					int lastChecksumFrame = RCGRollback.MainTimeFrames.LastChecksumFrame;
					RCGRollback.MainTimeFrames.LastChecksumFrame = RCGRollback.MainTimeFrames.MaxSimulated;
					int num = Mathf.Max(RCGRollback.MainTimeFrames.MaxSimulated - RCGRollback.System.FPS, RCGRollback.MainTimeFrames.MinimumFrame);
					byte[] raw = RCGRollback.GetRaw(num, true);
					if (raw != null)
					{
						int num2 = 0;
						Sync_Checksum.Post(GlobalTargets.OnlyServer, ReliabilityModes.ReliableOrdered, raw.ToInt32(ref num2), raw.ToInt32(ref num2), num);
					}
					else
					{
						Debug.LogError(string.Concat(new object[]
						{
							"Unable to sync, lastCheck ",
							lastChecksumFrame,
							" frame ",
							num,
							" min[",
							RCGRollback.MainTimeFrames.MinimumFrame,
							"] L[",
							RCGRollback.MainTimeFrames.MaxSimulated,
							"]"
						}));
					}
				}
			}
			BoltLinker.PushStat(simulationStats);
		}

		// Token: 0x06002F23 RID: 12067 RVA: 0x0001EED9 File Offset: 0x0001D0D9
		public void AdvanceBackgroundSim(int goal, ref SimulationStats stats)
		{
			RCGRollback.AdvanceBackgroundSimulation(RCGRollback.BackgroundTimeFrames.CurrentSimulate, goal, BoltLinker.m_maxBackgroundSimFrames, ref stats);
			RCGRollback.BackgroundTimeFrames.CurrentSimulate = RCGRollback.BackgroundIterator.WriteFrame;
			RCGRollback.BackgroundTimeFrames.MaxSimulated = RCGRollback.BackgroundIterator.WriteFrame;
		}

		// Token: 0x06002F24 RID: 12068 RVA: 0x000D61F0 File Offset: 0x000D43F0
		private void UpdateMetaState(int frame)
		{
			if (frame - BoltLinker.m_mainMetaTimeFrames.CurrentSimulate > 1800)
			{
				Debug.Log("Migrating Meta Frame Due to Time diff");
				SimulationIteration metaIteration = RCGRollback.MetaIteration;
				RCGRollback.MetaIteration.WriteFrame = frame;
				metaIteration.ReadFrame = frame;
				RCGRollback.MetaIteration.Write(RCGRollback.MetaSimulation[frame]);
				BoltLinker.m_mainMetaTimeFrames.SetAll(frame);
			}
			RCGRollback.AdvanceMetaSimulation(BoltLinker.m_mainMetaTimeFrames.CurrentSimulate, frame);
			RCGRollback.SimulationTimeFrames mainMetaTimeFrames = BoltLinker.m_mainMetaTimeFrames;
			BoltLinker.m_mainMetaTimeFrames.MaxSimulated = frame;
			mainMetaTimeFrames.CurrentSimulate = frame;
			RCGRollback.MetaIteration.GetEntities<MetaPlayerState>(out this.m_metaPlayers);
			for (int i = 0; i < this.m_metaPlayers.RestrictedCount; i++)
			{
				if (this.m_metaPlayers[i].m_connectionID == BoltLinker.MyConnectionID)
				{
					this.m_metaPlayer = this.m_metaPlayers[i];
				}
				if (BoltNetwork.IsServer && this.m_metaPlayers[i].m_loadingTime >= 30f)
				{
					foreach (BoltConnection boltConnection in BoltNetwork.Connections)
					{
						if (boltConnection.ConnectionId == this.m_metaPlayers[i].m_connectionID)
						{
							boltConnection.Disconnect();
						}
					}
				}
			}
			this.metaServerState = RCGRollback.MetaIteration.GetFirst<MetaServerState>();
			if (this.m_metaPlayer != null)
			{
				if (this.m_metaPlayer.m_desynced_meta && this.m_desyncStateMeta == BoltLinker.DesyncState.Synced)
				{
					Debug.LogError("Desynced meta at " + RCGRollback.MetaIteration.WriteFrame);
					if (this.m_syncRoutineMeta != null)
					{
						base.StopCoroutine(this.m_syncRoutineMeta);
					}
					this.m_syncRoutineMeta = base.StartCoroutine(this.MetaStateDownload());
				}
				else if (!this.m_metaPlayer.m_desynced_meta && !BoltNetwork.IsServer && BoltLinker.m_mainMetaTimeFrames.LastChecksumFrame + BoltLinker.ChecksumPeriod < BoltLinker.m_mainMetaTimeFrames.MaxSimulated)
				{
					int lastChecksumFrame = BoltLinker.m_mainMetaTimeFrames.LastChecksumFrame;
					BoltLinker.m_mainMetaTimeFrames.LastChecksumFrame = BoltLinker.m_mainMetaTimeFrames.MaxSimulated;
					int num = Mathf.Max(BoltLinker.m_mainMetaTimeFrames.MaxSimulated - RCGRollback.System.FPS, BoltLinker.m_mainMetaTimeFrames.MinimumFrame);
					byte[] metaRaw = RCGRollback.GetMetaRaw(num, true);
					if (metaRaw != null)
					{
						int num2 = 0;
						Meta_Checksum.Post(GlobalTargets.OnlyServer, ReliabilityModes.ReliableOrdered, metaRaw.ToInt32(ref num2), metaRaw.ToInt32(ref num2), num);
					}
					else
					{
						Debug.LogError(string.Concat(new object[]
						{
							"Unable to sync meta, lastCheck ",
							lastChecksumFrame,
							" frame ",
							num,
							" min[",
							BoltLinker.m_mainMetaTimeFrames.MinimumFrame,
							"] L[",
							BoltLinker.m_mainMetaTimeFrames.MaxSimulated,
							"]"
						}));
					}
				}
				if (this.metaServerState != null && !this.m_metaPlayer.m_loading)
				{
					if (this.myLevelState != this.metaServerState.m_levelState)
					{
						this.myLevelState = this.metaServerState.m_levelState;
						Debug.LogError("My level state mismatch");
						BoltLinker.AddMetaEvent<SetMetaPlayerLevelState>(new SetMetaPlayerLevelState
						{
							m_connectionID = BoltLinker.MyConnectionID,
							m_levelState = this.metaServerState.m_levelState
						}, -1);
						BoltLinker.AddMetaEvent<SetMetaPlayerLoading>(new SetMetaPlayerLoading
						{
							m_connectionID = BoltLinker.MyConnectionID,
							m_loading = true
						}, -1);
						if (!BoltNetwork.IsServer)
						{
							if (this.m_desyncStateMain != BoltLinker.DesyncState.Synced)
							{
								RCGRollback.BackgroundSimulating = false;
								if (this.m_syncRoutineMain != null)
								{
									base.StopCoroutine(this.m_syncRoutineMain);
								}
							}
							this.m_desyncStateMain = BoltLinker.DesyncState.Downloading;
							BoltLinker.AddMetaEvent<SetMetaPlayerDesyncedMain>(new SetMetaPlayerDesyncedMain
							{
								m_connectionID = BoltLinker.MyConnectionID,
								m_desynced_main = true
							}, -1);
						}
						if (global::Singleton<ScreenManager>.instance.Find<UI_Loading>(false) == null)
						{
							if (BoltNetwork.IsServer)
							{
								global::Singleton<ScreenManager>.instance.AddMenu<UI_Loading>(UI_Loading.SeverLoadScene(this.metaServerState.m_level), false);
							}
							else
							{
								global::Singleton<ScreenManager>.instance.AddMenu<UI_Loading>(UI_Loading.LoadNewScene(), false);
							}
						}
						RCGRollback.AdvanceMetaSimulation(BoltLinker.m_mainMetaTimeFrames.CurrentSimulate, frame);
						RCGRollback.SimulationTimeFrames mainMetaTimeFrames2 = BoltLinker.m_mainMetaTimeFrames;
						BoltLinker.m_mainMetaTimeFrames.MaxSimulated = frame;
						mainMetaTimeFrames2.CurrentSimulate = frame;
						return;
					}
					if (this.m_metaPlayer.m_desynced_main && this.m_desyncStateMain == BoltLinker.DesyncState.Synced)
					{
						this.m_desyncStateMain = BoltLinker.DesyncState.Downloading;
						if (BoltLinker.Instance.m_syncRoutineMain != null)
						{
							Debug.LogError("Orphaned Sync Process?");
							base.StopCoroutine(this.m_syncRoutineMain);
						}
						this.m_syncRoutineMain = base.StartCoroutine(this.BeginSync());
					}
				}
			}
		}

		// Token: 0x06002F25 RID: 12069 RVA: 0x000D6680 File Offset: 0x000D4880
		private void HandleInput(int thisFrame)
		{
			if (this.InputFrame < thisFrame)
			{
				this.InputFrame = thisFrame;
				for (int i = 0; i < BoltLinker.m_localControllers.Count; i++)
				{
					BoltLinker.LocalControllerData localControllerData = BoltLinker.m_localControllers[i];
					InputMasks currentInput = localControllerData.m_currentInput;
					if (currentInput != localControllerData.m_lastInputSent)
					{
						BoltLinker.AddEvent<PlayerInputEvent>(new PlayerInputEvent
						{
							m_controllerEntityID = localControllerData.m_controllerSimualtionID,
							masks = currentInput
						}, this.InputFrame + this.CurrentInputDelay - 1);
						localControllerData.m_lastInputSent = currentInput;
						localControllerData.m_lastInputFrame = this.InputFrame;
					}
					localControllerData.m_currentInput = default(InputMasks);
				}
			}
		}

		// Token: 0x06002F26 RID: 12070 RVA: 0x0001EF19 File Offset: 0x0001D119
		private void LateUpdate()
		{
			RCGRollback.LateUpdate();
		}

		// Token: 0x06002F27 RID: 12071 RVA: 0x0001EF20 File Offset: 0x0001D120
		private void OnDrawGizmos()
		{
			RCGRollback.System.DrawGizmos();
		}

		// Token: 0x06002F28 RID: 12072 RVA: 0x000D6724 File Offset: 0x000D4924
		public static int GetSceneIndex(string name)
		{
			int num;
			if (BoltLinker.SceneIndices.TryGetValue(name, out num))
			{
				return num;
			}
			for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
			{
				if (BoltLinker.GetSceneNameFromIndex(i) == name)
				{
					BoltLinker.SceneIndices.Add(name, i);
					return i;
				}
			}
			return -1;
		}

		// Token: 0x06002F29 RID: 12073 RVA: 0x000D6770 File Offset: 0x000D4970
		public static string GetSceneNameFromIndex(int BuildIndex)
		{
			string scenePathByBuildIndex = SceneUtility.GetScenePathByBuildIndex(BuildIndex);
			if (string.IsNullOrEmpty(scenePathByBuildIndex))
			{
				return "";
			}
			int num = scenePathByBuildIndex.LastIndexOf('/');
			string text = scenePathByBuildIndex.Substring(num + 1);
			int num2 = text.LastIndexOf('.');
			return text.Substring(0, num2);
		}

		// Token: 0x06002F2A RID: 12074 RVA: 0x0001EF2C File Offset: 0x0001D12C
		public override void OnEvent(MovieSkip evnt)
		{
			global::Singleton<RCGMoviePlayer>.instance.DoSkip();
		}

		// Token: 0x0400280D RID: 10253
		public static bool LogStuff;

		// Token: 0x0400280E RID: 10254
		private static bool isReturningFromSleep = false;

		// Token: 0x0400280F RID: 10255
		private static bool hasSubscribedToSwitchNotifications = false;

		// Token: 0x04002810 RID: 10256
		public static bool MovieHack = false;

		// Token: 0x04002811 RID: 10257
		private static List<BoltLinker.LocalControllerData> m_localControllers = new List<BoltLinker.LocalControllerData>();

		// Token: 0x04002812 RID: 10258
		private const int StatAmount = 60;

		// Token: 0x04002813 RID: 10259
		private static List<SimulationStats> stats = new List<SimulationStats>(60);

		// Token: 0x04002814 RID: 10260
		private static int current = 0;

		// Token: 0x04002815 RID: 10261
		private static long m_lastTime;

		// Token: 0x04002816 RID: 10262
		public static bool InitiatedDisconnect;

		// Token: 0x04002817 RID: 10263
		public static bool Kicked = false;

		// Token: 0x04002818 RID: 10264
		public static bool Blocked = false;

		// Token: 0x04002819 RID: 10265
		public static bool LoggedOff = false;

		// Token: 0x0400281A RID: 10266
		public static bool InternetConnection = true;

		// Token: 0x0400281B RID: 10267
		private const string _mainMenuScene = "MainMenu";

		// Token: 0x0400281C RID: 10268
		public static BoltLinker.SessionUpdateEvent SessionListUpdatedEvent;

		// Token: 0x0400281D RID: 10269
		private static int m_incomingPosition;

		// Token: 0x0400281E RID: 10270
		private static byte[] m_incomingBytes = new byte[71936];

		// Token: 0x0400281F RID: 10271
		private byte[] ChecksumBuilder = new byte[8];

		// Token: 0x04002820 RID: 10272
		public const float MinPing = 0.05f;

		// Token: 0x04002821 RID: 10273
		public const float MaxPing = 0.2f;

		// Token: 0x04002822 RID: 10274
		public Coroutine m_syncRoutineMain;

		// Token: 0x04002823 RID: 10275
		public Coroutine m_syncRoutineMeta;

		// Token: 0x04002824 RID: 10276
		public int ServerDelay;

		// Token: 0x04002825 RID: 10277
		public static int UserDelay;

		// Token: 0x04002826 RID: 10278
		public const int PingSamples = 20;

		// Token: 0x04002827 RID: 10279
		private static Dictionary<BoltConnection, BoltLinker.ConnectionData> pings = new Dictionary<BoltConnection, BoltLinker.ConnectionData>();

		// Token: 0x04002828 RID: 10280
		private static List<float> tempCalc = new List<float>();

		// Token: 0x04002829 RID: 10281
		private static Dictionary<int, Simulation.ServerTimeSample> samples = new Dictionary<int, Simulation.ServerTimeSample>();

		// Token: 0x0400282A RID: 10282
		private static BoltLinker.SyncRequest m_currentRequest;

		// Token: 0x0400282B RID: 10283
		private static BoltLinker.SyncRequest m_metaRequest;

		// Token: 0x0400282C RID: 10284
		public int m_metaState;

		// Token: 0x0400282D RID: 10285
		private static int StateRequestID;

		// Token: 0x0400282E RID: 10286
		private static int m_maxBackgroundSimFrames = 3;

		// Token: 0x0400282F RID: 10287
		public bool DoubleSyncFinished;

		// Token: 0x04002830 RID: 10288
		public static RCGRollback.SimulationTimeFrames m_mainMetaTimeFrames = new RCGRollback.SimulationTimeFrames();

		// Token: 0x04002831 RID: 10289
		public int InputFrame;

		// Token: 0x04002832 RID: 10290
		public static BoltLinker Instance;

		// Token: 0x04002833 RID: 10291
		private static Dictionary<BoltLinker.CacheEnum, SimulationFrame> m_serverCachedStates = new Dictionary<BoltLinker.CacheEnum, SimulationFrame>();

		// Token: 0x04002834 RID: 10292
		private static int PacketID = 0;

		// Token: 0x04002835 RID: 10293
		private int CurrentEventChunk = -1;

		// Token: 0x04002836 RID: 10294
		private static byte[] incoming = new byte[65536];

		// Token: 0x04002837 RID: 10295
		private static int pos;

		// Token: 0x04002838 RID: 10296
		private static byte[] evtWriteBuffer = new byte[65536];

		// Token: 0x04002839 RID: 10297
		private static BinaryPacket evtPacket = new BinaryPacket();

		// Token: 0x0400283A RID: 10298
		private static BoltLinker.CacheEnum m_reloadFromCache;

		// Token: 0x0400283B RID: 10299
		private static bool lookingforLoad;

		// Token: 0x0400283C RID: 10300
		private static IRestrictedList<SaveSlotEntity> slots;

		// Token: 0x0400283D RID: 10301
		private static IRestrictedList<PlayerControllerEntity> pces;

		// Token: 0x0400283E RID: 10302
		private List<int> RollbackFrames = new List<int> { 0 };

		// Token: 0x0400283F RID: 10303
		private int frameCount;

		// Token: 0x04002840 RID: 10304
		private float dt;

		// Token: 0x04002841 RID: 10305
		private float fps;

		// Token: 0x04002842 RID: 10306
		private float updateRate = 4f;

		// Token: 0x04002843 RID: 10307
		public static float syncFallbackTimer = 0f;

		// Token: 0x04002844 RID: 10308
		private static int ChecksumPeriod = 20;

		// Token: 0x04002845 RID: 10309
		private bool boost;

		// Token: 0x04002846 RID: 10310
		private static int pingUpdate = 2;

		// Token: 0x04002847 RID: 10311
		private float SinglePlayerTime;

		// Token: 0x04002848 RID: 10312
		private BoltLinker.DesyncState m_desyncStateMain;

		// Token: 0x04002849 RID: 10313
		private BoltLinker.DesyncState m_desyncStateMeta;

		// Token: 0x0400284A RID: 10314
		private IRestrictedList<MetaPlayerState> m_metaPlayers;

		// Token: 0x0400284B RID: 10315
		private MetaPlayerState m_metaPlayer;

		// Token: 0x0400284C RID: 10316
		private MetaServerState metaServerState;

		// Token: 0x0400284D RID: 10317
		private uint myLevelState;

		// Token: 0x0400284E RID: 10318
		private static Dictionary<string, int> SceneIndices = new Dictionary<string, int>();

		// Token: 0x02000645 RID: 1605
		public class ServerTime : IProtocolToken
		{
			// Token: 0x06002F31 RID: 12081 RVA: 0x00002191 File Offset: 0x00000391
			public ServerTime()
			{
			}

			// Token: 0x06002F32 RID: 12082 RVA: 0x0001EF97 File Offset: 0x0001D197
			public ServerTime(double elapsed)
			{
				this.m_serverElapsedTimeMilliseconds = elapsed;
			}

			// Token: 0x06002F33 RID: 12083 RVA: 0x0001EFA6 File Offset: 0x0001D1A6
			public void Read(UdpPacket packet)
			{
				this.m_serverElapsedTimeMilliseconds = packet.ReadDouble();
			}

			// Token: 0x06002F34 RID: 12084 RVA: 0x0001EFB4 File Offset: 0x0001D1B4
			public void Write(UdpPacket packet)
			{
				packet.WriteDouble(this.m_serverElapsedTimeMilliseconds);
			}

			// Token: 0x0400284F RID: 10319
			public double m_serverElapsedTimeMilliseconds;
		}

		// Token: 0x02000646 RID: 1606
		public class SyncRequest
		{
			// Token: 0x06002F35 RID: 12085 RVA: 0x0001EFC2 File Offset: 0x0001D1C2
			public SyncRequest(int ID)
			{
				this.m_stateRequestID = ID;
			}

			// Token: 0x04002850 RID: 10320
			public int m_stateRequestID;

			// Token: 0x04002851 RID: 10321
			public float m_time;

			// Token: 0x04002852 RID: 10322
			public bool m_finished;

			// Token: 0x04002853 RID: 10323
			public int readScene;
		}

		// Token: 0x02000647 RID: 1607
		private class LocalControllerData
		{
			// Token: 0x04002854 RID: 10324
			public uint m_controllerSimualtionID;

			// Token: 0x04002855 RID: 10325
			public uint m_playerEntityID;

			// Token: 0x04002856 RID: 10326
			public int LocalPlayerID;

			// Token: 0x04002857 RID: 10327
			public InputMasks m_currentInput;

			// Token: 0x04002858 RID: 10328
			public InputMasks m_lastInputSent;

			// Token: 0x04002859 RID: 10329
			public int m_lastInputFrame;
		}

		// Token: 0x02000648 RID: 1608
		// (Invoke) Token: 0x06002F38 RID: 12088
		public delegate void SessionUpdateEvent(Map<Guid, UdpSession> sessionList);

		// Token: 0x02000649 RID: 1609
		private class ConnectionData
		{
			// Token: 0x06002F3B RID: 12091 RVA: 0x0001EFD1 File Offset: 0x0001D1D1
			public void Add(float ping)
			{
				this.pings.Add(ping);
				if (this.pings.Count > 20)
				{
					this.pings.RemoveAt(0);
				}
			}

			// Token: 0x06002F3C RID: 12092 RVA: 0x000D67B4 File Offset: 0x000D49B4
			public void UpdatePlayer(bool twoPlayerGame)
			{
				int num = Mathf.Min(this.pings.Count, 20);
				BoltLinker.tempCalc.Clear();
				for (int i = 1; i < num - 1; i++)
				{
					BoltLinker.tempCalc.Add((this.pings[i - 1] + this.pings[i] + this.pings[i + 1]) / 3f);
				}
				float num2 = 0f;
				for (int j = 0; j < BoltLinker.tempCalc.Count; j++)
				{
					num2 += BoltLinker.tempCalc[j];
				}
				num2 /= (float)BoltLinker.tempCalc.Count;
				if (twoPlayerGame)
				{
					num2 /= 2f;
				}
				this.m_lastPing = num2;
				this.m_lastDelaySent = this.CalcInputDelay(this.m_lastPing);
			}

			// Token: 0x06002F3D RID: 12093 RVA: 0x0001EFFA File Offset: 0x0001D1FA
			private int CalcInputDelay(float ping)
			{
				return (int)(ping * (float)RCGRollback.System.FPS) + 2;
			}

			// Token: 0x0400285A RID: 10330
			private List<float> pings = new List<float>();

			// Token: 0x0400285B RID: 10331
			public float m_lastPing;

			// Token: 0x0400285C RID: 10332
			public int m_lastDelaySent;
		}

		// Token: 0x0200064A RID: 1610
		private enum PacketType
		{
			// Token: 0x0400285E RID: 10334
			Event,
			// Token: 0x0400285F RID: 10335
			MetaEvent,
			// Token: 0x04002860 RID: 10336
			MetaState,
			// Token: 0x04002861 RID: 10337
			GameState
		}

		// Token: 0x0200064B RID: 1611
		public enum CacheEnum
		{
			// Token: 0x04002863 RID: 10339
			None,
			// Token: 0x04002864 RID: 10340
			Hideout,
			// Token: 0x04002865 RID: 10341
			MissionStart,
			// Token: 0x04002866 RID: 10342
			MissionCheckpoint,
			// Token: 0x04002867 RID: 10343
			Scene
		}

		// Token: 0x0200064C RID: 1612
		private enum DesyncState
		{
			// Token: 0x04002869 RID: 10345
			Synced,
			// Token: 0x0400286A RID: 10346
			Downloading,
			// Token: 0x0400286B RID: 10347
			Simulating
		}
	}
}
