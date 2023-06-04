using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using FMOD.Studio;
using FMODAudioMap;
using I2.Loc;
using NaughtyAttributes;
using PropertySerializer;
using RCG.Environment.DayNight;
using RCG.ResourceMaps;
using RCG.Rollback;
using RCG.Rollback.Components;
using RCG.Rollback.Events;
using RCG.Rollback.Systems;
using RCG.UI.Screens.Widgets;
using TMPro;
using Tools.BinaryRollback;
using UnityEngine;
using UnityEngine.UI;

namespace RCG.UI.Screens
{
	// Token: 0x0200041B RID: 1051
	public class UI_CellPhone : UIScreen
	{
		// Token: 0x17000715 RID: 1813
		// (get) Token: 0x06001FC0 RID: 8128 RVA: 0x00014A79 File Offset: 0x00012C79
		// (set) Token: 0x06001FC1 RID: 8129 RVA: 0x00014A81 File Offset: 0x00012C81
		public uint PlayerControllerNetID { get; protected set; }

		// Token: 0x17000716 RID: 1814
		// (get) Token: 0x06001FC2 RID: 8130 RVA: 0x00014A8A File Offset: 0x00012C8A
		// (set) Token: 0x06001FC3 RID: 8131 RVA: 0x00014A92 File Offset: 0x00012C92
		public int PlayerControllerLocalID { get; protected set; }

		// Token: 0x17000717 RID: 1815
		// (get) Token: 0x06001FC4 RID: 8132 RVA: 0x00014A9B File Offset: 0x00012C9B
		// (set) Token: 0x06001FC5 RID: 8133 RVA: 0x00014AA3 File Offset: 0x00012CA3
		public string PlayerClassName { get; private set; }

		// Token: 0x06001FC6 RID: 8134
		public override void OnOpen(Container data)
		{
			base.OnOpen(data);
			SimulationIteration simulationIteration = BoltLinker.OpenCurrentFrame();
			data.TryGet<NetworkState>("NetworkState", out this.m_networkState, NetworkState.Offline);
			data.TryGet<uint>("PlayerID", out this.m_playerNetId, 0U);
			data.TryGet<uint>("ControllerID", out this.m_contollerNetId, 0U);
			data.TryGet<bool>("MapShortcut", out this.m_mapScreenShortcut, false);
			data.TryGet<bool>("OnlineShortcut", out this.m_onlineScreenShortcut, false);
			PlayerEntity playerEntity;
			PlayerControllerEntity playerControllerEntity;
			SaveSlotEntity saveSlotEntity;
			this.GetPlayerEntity(out playerEntity, out playerControllerEntity, out saveSlotEntity);
			if (playerEntity == null)
			{
				return;
			}
			this.PlayerControllerNetID = playerEntity.Controller(simulationIteration).NetID;
			this.PlayerControllerLocalID = playerEntity.Controller(simulationIteration).LocalID;
			this.PlayerClassName = playerEntity.ClassName;
			this.playerInputMask = 1 << this.PlayerControllerLocalID;
			this.m_onlineScreenButton.gameObject.SetActive(!BoltNetwork.IsSinglePlayer);
			this.SetPhoneFrameForCharacter();
			this.UpdatePhoneUserName();
			this.CheckForNotifications(simulationIteration);
			this._transitionAnimator.Play("Phone Transition Reset");
			base.StartCoroutine(this.CellphoneAppearCoroutine());
			RectTransform component = this.PhoneObject.GetComponent<RectTransform>();
			if (!this.m_setStartPosAndRot)
			{
				this.m_startPosition = component.localPosition;
				this.m_startRotation = component.localEulerAngles;
				this.m_setStartPosAndRot = true;
			}
			else
			{
				component.localPosition = this.m_startPosition;
				component.localEulerAngles = this.m_startRotation;
				component.localScale = Vector3.one;
				this._transScreenFill.sizeDelta = Vector2.zero;
				this._transMaskEnd.anchoredPosition = Vector2.zero;
			}
			this.m_homeScreen.m_cellphoneScreen = this;
			this.m_homeScreen.m_audiomap = this.m_audiomap;
			this.m_homeScreen.ResetInitialSelectable();
			this.SetClock();
			this.m_footer.UpdateFooter(this.m_homeScreen.FooterPrompts(), this.m_homeScreen.m_useIcon);
			this.OpenScreen(this.m_homeScreen);
			if (this.m_onlineScreenShortcut)
			{
				base.ForceSelection(this.m_onlineScreenButton);
			}
			else if (this.m_mapScreenShortcut || this.HasScenestoShowOnMapScreen())
			{
				base.ForceSelection(this.m_mapScreenSelectable);
			}
			else if (this.m_showHonkrOnOpen)
			{
				base.ForceSelection(this.m_honkrScreenSelectable);
			}
			if (!this.m_snapshot.isValid())
			{
				this.m_snapshot = RCGAudio.instance.CreateEventInstance(this.m_audiomap.GetAudioEvent("Pause_Snapshot"));
			}
			this.SnapshotPlayback(this.m_snapshot, true);
			this.PlaySFX("Pause");
			RumbleSystem.AddLocalRumble(RumbleMap.Instance.GetNow<RumbleData>(Animator.StringToHash("RumbleData_pause")), playerControllerEntity.LocalID);
		}

		// Token: 0x06001FC7 RID: 8135 RVA: 0x00014AAC File Offset: 0x00012CAC
		private UI_CellPhone_SubScreen CreateSubscreen(UI_CellPhone_SubScreen subscreenPrefab, ref GameObject parent)
		{
			UI_CellPhone_SubScreen ui_CellPhone_SubScreen = UnityEngine.Object.Instantiate<UI_CellPhone_SubScreen>(subscreenPrefab, parent.transform);
			ui_CellPhone_SubScreen.m_cellphoneScreen = this;
			ui_CellPhone_SubScreen.m_audiomap = this.m_audiomap;
			ui_CellPhone_SubScreen.gameObject.SetActive(false);
			return ui_CellPhone_SubScreen;
		}

		// Token: 0x06001FC8 RID: 8136 RVA: 0x00099840 File Offset: 0x00097A40
		public void UpdatePhoneUserName()
		{
			if (!string.IsNullOrEmpty(this.PlayerClassName))
			{
				this.m_phoneUserName = string.Format("Sheet1/Ticker_{0}_NAME", this.PlayerClassName);
				this.m_phoneUserNameText.SetText(LocalizationManager.GetTermTranslation(this.m_phoneUserName, true, 0, true, false, null, null, true));
			}
		}

		// Token: 0x06001FC9 RID: 8137 RVA: 0x00014ADA File Offset: 0x00012CDA
		private IEnumerator CellphoneAppearCoroutine()
		{
			this.m_disableInput = true;
			RectTransform component = base.GetComponent<RectTransform>();
			component.localScale = Vector3.one;
			component.anchoredPosition = new Vector2(-1500f, 0f);
			component.DOAnchorPos(Vector3.zero, 0.25f, false);
			component.localEulerAngles = new Vector3(0f, 0f, 25f);
			component.DORotate(new Vector3(0f, 0f, 0f), 0.25f, RotateMode.Fast);
			yield return new WaitForSeconds(0.25f);
			if (this.m_onlineScreenShortcut)
			{
				this.TransitionToOnlineScreen();
			}
			else if (this.m_mapScreenShortcut || this.HasScenestoShowOnMapScreen())
			{
				this.TransitionToMapScreen(false);
			}
			else if (this.m_showHonkrOnOpen)
			{
				this.TransitionToHonkrScreen(false);
				this.m_showHonkrOnOpen = false;
			}
			else
			{
				this.m_disableInput = false;
			}
			yield break;
		}

		// Token: 0x06001FCA RID: 8138 RVA: 0x00014AE9 File Offset: 0x00012CE9
		protected bool HasScenestoShowOnMapScreen()
		{
			return this.m_scenesToShowOnMapScreen != null && this.m_scenesToShowOnMapScreen.Length != 0;
		}

		// Token: 0x06001FCB RID: 8139 RVA: 0x00014AFF File Offset: 0x00012CFF
		public void ShowSceneOnMapScreenFromHonk(string[] scenesToShow)
		{
			this.m_scenesToShowOnMapScreen = scenesToShow;
			if (this.m_scenesToShowOnMapScreen != null && this.m_scenesToShowOnMapScreen.Length != 0)
			{
				this.PlaySFX("Select_GPS");
				this.TransitionToMapScreen(true);
			}
		}

		// Token: 0x06001FCC RID: 8140 RVA: 0x00099890 File Offset: 0x00097A90
		public void UpdateLatencyIndicator(float connectionStrength)
		{
			if (BoltNetwork.IsSinglePlayer)
			{
				this.m_wifiSymbol.enabled = false;
				return;
			}
			int num = (int)(Mathf.Min(connectionStrength, 0.9999f) * (float)this.m_wifiIcons.Length);
			this.m_wifiSymbol.sprite = this.m_wifiIcons[num];
			this.m_wifiSymbol.enabled = true;
		}

		// Token: 0x06001FCD RID: 8141 RVA: 0x00014B2B File Offset: 0x00012D2B
		public override void Start()
		{
			base.Start();
			BoltLinker.OnBoltShutdown += this.OnBoltShutdown;
		}

		// Token: 0x06001FCE RID: 8142 RVA: 0x00014B44 File Offset: 0x00012D44
		private void OnDestroy()
		{
			this.m_viewedHonkrNameHashes.Clear();
			BoltLinker.OnBoltShutdown -= this.OnBoltShutdown;
		}

		// Token: 0x06001FCF RID: 8143 RVA: 0x00014B62 File Offset: 0x00012D62
		protected void OnBoltShutdown()
		{
			this.m_viewedHonkrNameHashes.Clear();
			if (this.m_movesListScreen != null)
			{
				this.m_movesListScreen.ResetMoveListScroll();
			}
		}

		// Token: 0x06001FD0 RID: 8144 RVA: 0x00014B88 File Offset: 0x00012D88
		public void OnEnable()
		{
			UIEventSystem.active.OnRenderIteration += this.OnRenderIteration;
			LocalizationManager.OnLocalizeEvent += this.OnLocalize;
		}

		// Token: 0x06001FD1 RID: 8145 RVA: 0x00014BB1 File Offset: 0x00012DB1
		public void OnDisable()
		{
			UIEventSystem.active.OnRenderIteration -= this.OnRenderIteration;
			LocalizationManager.OnLocalizeEvent -= this.OnLocalize;
		}

		// Token: 0x06001FD2 RID: 8146 RVA: 0x00014BDA File Offset: 0x00012DDA
		protected void OnLocalize()
		{
			this.UpdatePhoneUserName();
		}

		// Token: 0x06001FD3 RID: 8147 RVA: 0x000998E8 File Offset: 0x00097AE8
		private void OnRenderIteration(SimulationIteration iteration)
		{
			PlayerControllerEntity entity = iteration.GetEntity<PlayerControllerEntity>(this.m_contollerNetId);
			if (entity == null)
			{
				iteration.GetEntities<PlayerControllerEntity>(out this.remainingEntities);
				this.ClosePhone();
				return;
			}
			this.UpdateLatencyIndicator(entity.SaveSlot(iteration).m_connectionStrength);
			if (entity.Player(iteration) is PlayerEntity)
			{
				PlayerEntity playerEntity = entity.Player(iteration) as PlayerEntity;
				this.m_staminaMeter.SetValue(playerEntity.StaminaPercent(iteration));
				this.m_specialMeter.SetValue(playerEntity.SpecialPercent(iteration));
				this.UpdateExperienceAndLevel(playerEntity);
			}
			if (this.m_disableInput)
			{
				return;
			}
			if (entity == null || (entity.MenuType != UIMenu.Phone && entity.MenuType != UIMenu.PhoneMap && entity.MenuType != UIMenu.PhoneOnline) || !(entity.Player(iteration) is PlayerEntity))
			{
				this.ClosePhone();
			}
		}

		// Token: 0x06001FD4 RID: 8148 RVA: 0x000999AC File Offset: 0x00097BAC
		protected void UpdateExperienceAndLevel(PlayerEntity playerEntity)
		{
			float experience = playerEntity.m_baseData.Experience;
			int level = playerEntity.m_baseData.Level;
			float expRemainingForLevel = PlayerStatsProgression.Instance.GetExpRemainingForLevel(experience);
			this.m_levelValueText.SetText(level.ToString());
			this.m_experienceText.SetText(string.Format("{0}: {1}", LocalizationManager.GetTranslation("Sheet1/Cellphone_Home_Exp", true, 0, true, false, null, null, true), ((int)experience).ToString("D6")));
			this.m_nextLevelExperienceText.SetText(string.Format("{0}: {1}", LocalizationManager.GetTranslation("Sheet1/Cellphone_Home_Next", true, 0, true, false, null, null, true), ((int)expRemainingForLevel).ToString("D6")));
		}

		// Token: 0x06001FD5 RID: 8149 RVA: 0x00014BE2 File Offset: 0x00012DE2
		public void GetPlayerEntity(out PlayerEntity player, out PlayerControllerEntity controller, out SaveSlotEntity saveslot)
		{
			this.GetPlayerEntity(BoltLinker.OpenCurrentFrame(), out player, out controller, out saveslot);
		}

		// Token: 0x06001FD6 RID: 8150 RVA: 0x00014BF2 File Offset: 0x00012DF2
		public void GetPlayerEntity(SimulationIteration _iteration, out PlayerEntity player, out PlayerControllerEntity controller, out SaveSlotEntity saveslot)
		{
			player = _iteration.GetEntity<PlayerEntity>(this.m_playerNetId);
			controller = player.Controller(_iteration) as PlayerControllerEntity;
			saveslot = controller.SaveSlot(_iteration);
		}

		// Token: 0x06001FD7 RID: 8151 RVA: 0x00099A5C File Offset: 0x00097C5C
		public void CheckForNotifications(SimulationIteration iteration)
		{
			if (this.HasScenestoShowOnMapScreen())
			{
				return;
			}
			HonkrNotificationEntity first = iteration.GetFirst<HonkrNotificationEntity>();
			if (first != null)
			{
				for (int i = 0; i < first.m_honkBacklog.Count; i++)
				{
					if (first.m_honkBacklog[i] != null && !this.m_viewedHonkrNameHashes.Contains(first.m_honkBacklog[i].m_honkrMessageNameHash))
					{
						Data_HonkrMessage now = DatabaseMap.Instance.GetNow<HonkrMessageMap>(DatabaseType.HonkrMessages).GetNow<Data_HonkrMessage>(first.m_honkBacklog[i].m_honkrMessageNameHash);
						if (now.HasLocationsToShowOnMap())
						{
							this.m_scenesToShowOnMapScreen = now.m_locationsToShowOnMap;
							this.m_showHonkrOnOpen = false;
						}
						this.m_viewedHonkrNameHashes.Add(first.m_honkBacklog[i].m_honkrMessageNameHash);
						return;
					}
				}
			}
		}

		// Token: 0x06001FD8 RID: 8152 RVA: 0x00099B24 File Offset: 0x00097D24
		public void SetPhoneFrameForCharacter()
		{
			string playerClassName = this.PlayerClassName;
			if (playerClassName == "Misako")
			{
				this._frameImage.sprite = this._frameSprites[0];
				this._homeCharacterPortrait.sprite = this._homeCharacterSprites[0];
				return;
			}
			if (playerClassName == "Kyoko")
			{
				this._frameImage.sprite = this._frameSprites[1];
				this._homeCharacterPortrait.sprite = this._homeCharacterSprites[1];
				return;
			}
			if (playerClassName == "Kunio")
			{
				this._frameImage.sprite = this._frameSprites[2];
				this._homeCharacterPortrait.sprite = this._homeCharacterSprites[2];
				return;
			}
			if (playerClassName == "Riki")
			{
				this._frameImage.sprite = this._frameSprites[3];
				this._homeCharacterPortrait.sprite = this._homeCharacterSprites[3];
				return;
			}
			if (playerClassName == "Marian")
			{
				this._frameImage.sprite = this._frameSprites[4];
				this._homeCharacterPortrait.sprite = this._homeCharacterSprites[4];
				return;
			}
			if (!(playerClassName == "Provie"))
			{
				return;
			}
			this._frameImage.sprite = this._frameSprites[5];
			this._homeCharacterPortrait.sprite = this._homeCharacterSprites[5];
		}

		// Token: 0x06001FD9 RID: 8153 RVA: 0x00014C1C File Offset: 0x00012E1C
		public override void ButtonHold(int playerID, PlayerInput input)
		{
			if (this.m_disableInput)
			{
				return;
			}
			if (this.m_currentScreen.ButtonHold(playerID, input))
			{
				base.ButtonHold(playerID, input);
			}
		}

		// Token: 0x06001FDA RID: 8154 RVA: 0x00014C3E File Offset: 0x00012E3E
		protected void DisableInput(float _duration)
		{
			base.StartCoroutine(this.DisableInputCoroutine(_duration));
		}

		// Token: 0x06001FDB RID: 8155 RVA: 0x00014C4E File Offset: 0x00012E4E
		protected IEnumerator DisableInputCoroutine(float _duration)
		{
			this.m_disableInput = true;
			yield return new WaitForSeconds(_duration);
			this.m_disableInput = false;
			yield break;
		}

		// Token: 0x06001FDC RID: 8156 RVA: 0x00099C7C File Offset: 0x00097E7C
		public void SetClock()
		{
			string text = DayNightManager.DayNightMilitaryTime.ToString("0000");
			string text2 = " AM";
			int num = int.Parse(text[0].ToString() + text[1].ToString());
			int num2 = int.Parse(text[2].ToString() + text[3].ToString());
			if (num > 11)
			{
				num -= 12;
				text2 = " PM";
			}
			if (num == 0)
			{
				num = 12;
			}
			this._textClock.text = num.ToString() + ":" + num2.ToString("00") + text2;
		}

		// Token: 0x06001FDD RID: 8157
		public override void ButtonInput(int playerID, PlayerInput input)
		{
			if (this.m_disableInput)
			{
				return;
			}
			playerID = 2;
			this.m_prevSelection = base.CurrentSelection;
			UI_CellPhone_SubScreen currentScreen = this.m_currentScreen;
			if (currentScreen != null)
			{
				currentScreen.PassPrevSelected(this.m_prevSelection);
			}
			if (input == InputManager.Cancel)
			{
				this.m_currentScreen.OnBackPresssed(playerID);
				return;
			}
			if (input == PlayerInput.Start)
			{
				this.SendClosePhoneEvent();
				base.ButtonInput(playerID, input);
				return;
			}
			if (this.m_currentScreen == this.m_homeScreen)
			{
				base.ButtonInput(playerID, input);
				this.m_homeScreen.ButtonInput(playerID, input);
				return;
			}
			if (this.m_currentScreen.ButtonInput(playerID, input))
			{
				base.ButtonInput(playerID, input);
				this.m_currentScreen.ProcessButtonInput(playerID, input);
			}
		}

		// Token: 0x06001FDE RID: 8158 RVA: 0x00014C64 File Offset: 0x00012E64
		public override void ButtonUp(int playerID, PlayerInput input)
		{
			if (this.m_currentScreen != null && this.m_currentScreen.ButtonUp(playerID, input))
			{
				base.ButtonUp(playerID, input);
			}
		}

		// Token: 0x06001FDF RID: 8159 RVA: 0x00014C8B File Offset: 0x00012E8B
		public void SendClosePhoneEvent()
		{
			BoltLinker.AddEvent<SetIsMenuing>(new SetIsMenuing
			{
				m_controllerID = this.m_contollerNetId
			}, -1);
		}

		// Token: 0x06001FE0 RID: 8160 RVA: 0x00014CA4 File Offset: 0x00012EA4
		public override void OnClose()
		{
			this.SnapshotPlayback(this.m_snapshot, false);
		}

		// Token: 0x06001FE1 RID: 8161 RVA: 0x00099DE8 File Offset: 0x00097FE8
		private void ClosePhone()
		{
			UI_CellPhone_Snapshot ui_CellPhone_Snapshot = this.m_currentScreen as UI_CellPhone_Snapshot;
			if (ui_CellPhone_Snapshot != null)
			{
				ui_CellPhone_Snapshot.SlideImageOut(true);
			}
			UI_Navigation ui_Navigation = Singleton<ScreenManager>.instance.Find<UI_Navigation>(true);
			if (ui_Navigation != null)
			{
				ui_Navigation.Close();
			}
			base.StartCoroutine(this.ClosePhoneCoroutine());
			this.PlaySFX("Unpause");
		}

		// Token: 0x06001FE2 RID: 8162 RVA: 0x00014CB3 File Offset: 0x00012EB3
		private IEnumerator ClosePhoneCoroutine()
		{
			base.GetComponent<RectTransform>().DOAnchorPos(new Vector2(-1500f, 0f), 0.25f, false);
			base.GetComponent<RectTransform>().DORotate(new Vector3(0f, 0f, -25f), 0.05f, RotateMode.Fast);
			this.m_disableInput = true;
			yield return new WaitForSeconds(0.25f);
			this.SnapshotPlayback(this.m_snapshot, false);
			this.m_disableInput = false;
			this.m_currentScreen.Close();
			Singleton<ScreenManager>.instance.CloseMenu<UI_CellPhone>(this, null);
			yield break;
		}

		// Token: 0x06001FE3 RID: 8163 RVA: 0x00099E40 File Offset: 0x00098040
		private bool IsCurrentSelectionHomeButton()
		{
			for (int i = 0; i < this.m_homeButtons.Length; i++)
			{
				if (base.CurrentSelection == this.m_homeButtons[i])
				{
					return true;
				}
			}
			return false;
		}

		// Token: 0x06001FE4 RID: 8164 RVA: 0x00099E78 File Offset: 0x00098078
		public void IconDown(UI_CellPhone.CellphoneTransitions transitionType)
		{
			this.m_disableInput = true;
			if (this.IsCurrentSelectionHomeButton())
			{
				base.CurrentSelection.GetComponent<RectTransform>().DOScale(Vector3.one * 0.9f, 0.05f);
			}
			base.StartCoroutine(this.TransitionCoroutine(0.05f, 0.2f, transitionType));
		}

		// Token: 0x06001FE5 RID: 8165 RVA: 0x00014CC2 File Offset: 0x00012EC2
		protected IEnumerator TransitionCoroutine(float _initialDelay, float _transitionDelay, UI_CellPhone.CellphoneTransitions transitionType)
		{
			yield return new WaitForSeconds(_initialDelay);
			if (this.IsCurrentSelectionHomeButton())
			{
				base.CurrentSelection.GetComponent<RectTransform>().DOScale(Vector3.one, 0.05f);
			}
			this.m_disableInput = true;
			yield return new WaitForSeconds(_transitionDelay);
			this._transitionAnimator.Play(string.Format("{0} Transition Intro", transitionType));
			yield return null;
			while (!this._transitionAnimator.IsAnimFinished(0))
			{
				yield return null;
			}
			this.m_footer.UpdateFooter(this.m_screenToTransitionTo.FooterPrompts(), this.m_screenToTransitionTo.m_useIcon);
			this.OpenScreen(this.m_screenToTransitionTo);
			this._transitionAnimator.Play(string.Format("{0} Transition Outro", transitionType));
			yield return null;
			while (!this._transitionAnimator.IsAnimFinished(0))
			{
				yield return null;
			}
			this.m_disableInput = false;
			yield break;
		}

		// Token: 0x06001FE6 RID: 8166 RVA: 0x00014CE6 File Offset: 0x00012EE6
		private void OpenScreen(UI_CellPhone_SubScreen _screen)
		{
			if (this.m_currentScreen != null)
			{
				this.m_currentScreen.Close();
			}
			this.m_currentScreen = _screen;
			this.m_currentScreen.Open();
		}

		// Token: 0x06001FE7 RID: 8167 RVA: 0x00014D13 File Offset: 0x00012F13
		protected IEnumerator TransitionFromMapScreenCoroutine(bool toHomeScreen = true)
		{
			this.m_disableInput = true;
			RectTransform component = this.PhoneObject.GetComponent<RectTransform>();
			component.DOAnchorPos(this.m_startPosition, 0.25f, false);
			component.DOLocalRotate(Vector3.zero, 0.25f, RotateMode.Fast);
			component.DOScale(Vector3.one, 0.25f);
			yield return new WaitForSeconds(0.25f);
			this.OpenScreen(this.m_screenToTransitionTo);
			this._transitionAnimator.Play("Exit Transition Outro");
			yield return null;
			while (!this._transitionAnimator.IsAnimFinished(0))
			{
				yield return null;
			}
			if (toHomeScreen)
			{
				base.CurrentSelection = this.m_mapScreenSelectable;
				base.CurrentSelection.OnSelect(null);
			}
			yield return new WaitForSeconds(0.3f);
			this.m_disableInput = false;
			if (toHomeScreen)
			{
				PlayerControllerEntity entity = BoltLinker.OpenCurrentFrame().GetEntity<PlayerControllerEntity>(this.m_contollerNetId);
				if (entity != null && entity.MenuType == UIMenu.PhoneMap)
				{
					this.SendClosePhoneEvent();
				}
			}
			yield break;
		}

		// Token: 0x06001FE8 RID: 8168 RVA: 0x00099ED4 File Offset: 0x000980D4
		public void TransitionToHomeScreen(bool _transitioningFromMapScreen = false)
		{
			this.m_screenToTransitionTo = this.m_homeScreen;
			this.m_phoneUserNameText.SetText(LocalizationManager.GetTermTranslation(this.m_phoneUserName, true, 0, true, false, null, null, true));
			if (_transitioningFromMapScreen)
			{
				base.StartCoroutine(this.TransitionFromMapScreenCoroutine(true));
				return;
			}
			base.StartCoroutine(this.TransitionCoroutine(0f, 0f, UI_CellPhone.CellphoneTransitions.Exit));
		}

		// Token: 0x06001FE9 RID: 8169 RVA: 0x00014D29 File Offset: 0x00012F29
		public void TransitionToAccessoryScreen()
		{
			if (this.m_accessoryScreen == null)
			{
				this.m_accessoryScreen = this.CreateSubscreen(this.m_accessoryScreenPrefab, ref this.m_belowTopLayer) as UI_CellPhone_Accessories;
			}
			this.m_screenToTransitionTo = this.m_accessoryScreen;
			this.IconDown(UI_CellPhone.CellphoneTransitions.Accessories);
		}

		// Token: 0x06001FEA RID: 8170 RVA: 0x00014D69 File Offset: 0x00012F69
		public void TransitionToInventoryScreen()
		{
			if (this.m_inventoryScreen == null)
			{
				this.m_inventoryScreen = this.CreateSubscreen(this.m_inventoryScreenPrefab, ref this.m_belowTopLayer) as UI_CellPhone_Inventory;
			}
			this.m_screenToTransitionTo = this.m_inventoryScreen;
			this.IconDown(UI_CellPhone.CellphoneTransitions.Inventory);
		}

		// Token: 0x06001FEB RID: 8171 RVA: 0x00099F34 File Offset: 0x00098134
		public void TransitionToHonkrScreen(bool _transitioningFromMapScreen = false)
		{
			if (this.m_honkrScreen == null)
			{
				this.m_honkrScreen = this.CreateSubscreen(this.m_honkrScreenPrefab, ref this.m_aboveTopLayer) as UI_CellPhone_Honkr;
			}
			this.m_screenToTransitionTo = this.m_honkrScreen;
			if (_transitioningFromMapScreen)
			{
				base.StartCoroutine(this.TransitionFromMapScreenCoroutine(false));
				return;
			}
			this.IconDown(UI_CellPhone.CellphoneTransitions.Honkr);
		}

		// Token: 0x06001FEC RID: 8172 RVA: 0x00014DA9 File Offset: 0x00012FA9
		public void TransitionToSnapshotScreen()
		{
			if (this.m_snapshotScreen == null)
			{
				this.m_snapshotScreen = this.CreateSubscreen(this.m_snapshotScreenPrefab, ref this.m_aboveTopLayer) as UI_CellPhone_Snapshot;
			}
			this.m_screenToTransitionTo = this.m_snapshotScreen;
			this.IconDown(UI_CellPhone.CellphoneTransitions.Snapr);
		}

		// Token: 0x06001FED RID: 8173 RVA: 0x00099F94 File Offset: 0x00098194
		public void TransitionToSettingsScreen(bool _transitioningFromControlMapperScreen = false)
		{
			if (this.m_settingsScreen == null)
			{
				this.m_settingsScreen = this.CreateSubscreen(this.m_settingsScreenPrefab, ref this.m_belowTopLayer) as UI_CellPhone_Settings;
			}
			this.m_screenToTransitionTo = this.m_settingsScreen;
			if (_transitioningFromControlMapperScreen)
			{
				base.StartCoroutine(this.TransitionFromMapScreenCoroutine(false));
				return;
			}
			this.IconDown(UI_CellPhone.CellphoneTransitions.Settings);
		}

		// Token: 0x06001FEE RID: 8174 RVA: 0x00014DE9 File Offset: 0x00012FE9
		public void TransitionToMovesListScreen()
		{
			if (this.m_movesListScreen == null)
			{
				this.m_movesListScreen = this.CreateSubscreen(this.m_movesListScreenPrefab, ref this.m_belowTopLayer) as UI_CellPhone_MovesList;
			}
			this.m_screenToTransitionTo = this.m_movesListScreen;
			this.IconDown(UI_CellPhone.CellphoneTransitions.Movelist);
		}

		// Token: 0x06001FEF RID: 8175 RVA: 0x00014E29 File Offset: 0x00013029
		public void TransitionToOnlineScreen()
		{
			if (this.m_onlineScreen == null)
			{
				this.m_onlineScreen = this.CreateSubscreen(this.m_onlineScreenPrefab, ref this.m_aboveTopLayer) as UI_Cellphone_Online;
			}
			this.m_screenToTransitionTo = this.m_onlineScreen;
			this.IconDown(UI_CellPhone.CellphoneTransitions.Settings);
		}

		// Token: 0x06001FF0 RID: 8176 RVA: 0x00099FF4 File Offset: 0x000981F4
		public void TransitionToMapScreen(bool returnToHonkr = false)
		{
			if (this.m_mapScreen == null)
			{
				this.m_mapScreen = this.CreateSubscreen(this.m_mapScreenPrefab, ref this.m_belowTopLayer) as UI_CellPhone_Map;
			}
			this.m_screenToTransitionTo = this.m_mapScreen;
			base.StartCoroutine(this.TransitionToMapScreenCoroutine(returnToHonkr));
		}

		// Token: 0x06001FF1 RID: 8177 RVA: 0x00014E69 File Offset: 0x00013069
		protected IEnumerator TransitionToMapScreenCoroutine(bool returnToHonkr)
		{
			this.m_disableInput = true;
			if (!returnToHonkr && this.IsCurrentSelectionHomeButton())
			{
				base.CurrentSelection.GetComponent<RectTransform>().DOScale(Vector3.one * 0.9f, 0.05f);
				yield return new WaitForSeconds(0.05f);
				base.CurrentSelection.GetComponent<RectTransform>().DOScale(Vector3.one, 0.05f);
				yield return new WaitForSeconds(0.05f);
			}
			this._transitionAnimator.Play("Navigation Transition Intro");
			this.PutPhoneToFace();
			yield return null;
			while (!this._transitionAnimator.IsAnimFinished(0))
			{
				yield return null;
			}
			this._transitionAnimator.Play("Navigation Transition Outro");
			yield return null;
			while (!this._transitionAnimator.IsAnimFinished(0))
			{
				yield return null;
			}
			Singleton<ScreenManager>.instance.AddMenu<UI_Navigation>(new Container
			{
				{
					"NetworkState",
					NetworkState.Offline
				},
				{ "ScenesToHighlight", this.m_scenesToShowOnMapScreen },
				{ "ControllerLocalId", this.PlayerControllerLocalID },
				{ "ReturnToHonkr", returnToHonkr }
			}, false);
			this.m_scenesToShowOnMapScreen = null;
			this.m_disableInput = false;
			yield break;
		}

		// Token: 0x06001FF2 RID: 8178 RVA: 0x00014E7F File Offset: 0x0001307F
		public void TransitionToControllerRemapScreen()
		{
			base.StartCoroutine(this.TransitionToControllerRemapScreenCoroutine());
		}

		// Token: 0x06001FF3 RID: 8179 RVA: 0x00014E8E File Offset: 0x0001308E
		protected IEnumerator TransitionToControllerRemapScreenCoroutine()
		{
			this.m_disableInput = true;
			this.PlaySFX("Phone_Access");
			this._transitionAnimator.Play("Controller Transition Intro");
			this.PutPhoneToFace();
			yield return null;
			while (!this._transitionAnimator.IsAnimFinished(0))
			{
				yield return null;
			}
			this._transitionAnimator.Play("Controller Transition Outro");
			yield return null;
			while (!this._transitionAnimator.IsAnimFinished(0))
			{
				yield return null;
			}
			Singleton<ScreenManager>.instance.AddMenu<UI_ControlMapper>(new Container
			{
				{ "OpenedFromCellphone", true },
				{ "ControllerId", this.PlayerControllerLocalID }
			}, true);
			this.m_disableInput = false;
			yield break;
		}

		// Token: 0x06001FF4 RID: 8180 RVA: 0x0009A048 File Offset: 0x00098248
		protected void PutPhoneToFace()
		{
			RectTransform component = this.PhoneObject.GetComponent<RectTransform>();
			component.DOAnchorPos(new Vector2(-212f, 0f), 0.25f, false);
			component.DOLocalRotate(new Vector3(0f, 0f, -90f), 0.25f, RotateMode.Fast);
			component.DOScale(Vector3.one * 2.3f, 0.25f);
		}

		// Token: 0x06001FF5 RID: 8181 RVA: 0x0009A0B8 File Offset: 0x000982B8
		public void PlaySFX(string evt)
		{
			if (string.IsNullOrEmpty(evt))
			{
				return;
			}
			FModAudioMap audiomap = this.m_audiomap;
			if (string.IsNullOrEmpty((audiomap != null) ? audiomap.GetAudioEvent(evt) : null))
			{
				return;
			}
			RCGAudio instance = RCGAudio.instance;
			FModAudioMap audiomap2 = this.m_audiomap;
			instance.PlayOneShot((audiomap2 != null) ? audiomap2.GetAudioEvent(evt) : null, default(Vector3));
		}

		// Token: 0x06001FF6 RID: 8182 RVA: 0x00014E9D File Offset: 0x0001309D
		public void SnapshotPlayback(EventInstance _instance, bool _playbackState)
		{
			if (this.m_snapshot.isValid())
			{
				if (_playbackState)
				{
					_instance.start();
					return;
				}
				_instance.stop(STOP_MODE.ALLOWFADEOUT);
			}
		}

		// Token: 0x06001FF7 RID: 8183 RVA: 0x00014CA4 File Offset: 0x00012EA4
		public void QuitToMainMenu()
		{
			this.SnapshotPlayback(this.m_snapshot, false);
		}

		// Token: 0x0400193D RID: 6461
		[SerializeField]
		protected UI_CellPhone_Inventory m_inventoryScreenPrefab;

		// Token: 0x0400193E RID: 6462
		[SerializeField]
		protected UI_CellPhone_Accessories m_accessoryScreenPrefab;

		// Token: 0x0400193F RID: 6463
		[SerializeField]
		protected UI_CellPhone_Snapshot m_snapshotScreenPrefab;

		// Token: 0x04001940 RID: 6464
		[SerializeField]
		protected UI_CellPhone_Honkr m_honkrScreenPrefab;

		// Token: 0x04001941 RID: 6465
		[SerializeField]
		protected UI_CellPhone_Settings m_settingsScreenPrefab;

		// Token: 0x04001942 RID: 6466
		[SerializeField]
		protected UI_CellPhone_MovesList m_movesListScreenPrefab;

		// Token: 0x04001943 RID: 6467
		[SerializeField]
		protected UI_CellPhone_Map m_mapScreenPrefab;

		// Token: 0x04001944 RID: 6468
		[SerializeField]
		protected UI_Cellphone_Online m_onlineScreenPrefab;

		// Token: 0x04001945 RID: 6469
		[SerializeField]
		protected UI_CellPhone_Home m_homeScreen;

		// Token: 0x04001946 RID: 6470
		[SerializeField]
		protected GameObject m_belowTopLayer;

		// Token: 0x04001947 RID: 6471
		[SerializeField]
		protected GameObject m_aboveTopLayer;

		// Token: 0x04001948 RID: 6472
		[SerializeField]
		protected GameObject PhoneObject;

		// Token: 0x04001949 RID: 6473
		[SerializeField]
		protected UISelectable m_onlineScreenButton;

		// Token: 0x0400194A RID: 6474
		[SerializeField]
		public UI_Cellphone_Footer m_footer;

		// Token: 0x0400194B RID: 6475
		[BoxGroup("TopLayer")]
		[SerializeField]
		protected UI_Meter m_staminaMeter;

		// Token: 0x0400194C RID: 6476
		[BoxGroup("TopLayer")]
		[SerializeField]
		protected UI_Meter m_specialMeter;

		// Token: 0x0400194D RID: 6477
		[BoxGroup("TopLayer")]
		[SerializeField]
		protected TextMeshProUGUI _textClock;

		// Token: 0x0400194E RID: 6478
		[BoxGroup("TopLayer")]
		[SerializeField]
		protected TextMeshProUGUI m_phoneUserNameText;

		// Token: 0x0400194F RID: 6479
		[BoxGroup("TopLayer")]
		[SerializeField]
		protected Image _frameImage;

		// Token: 0x04001950 RID: 6480
		[BoxGroup("TopLayer")]
		[SerializeField]
		protected Image _homeCharacterPortrait;

		// Token: 0x04001951 RID: 6481
		[BoxGroup("TopLayer")]
		[SerializeField]
		protected Sprite[] _frameSprites;

		// Token: 0x04001952 RID: 6482
		[BoxGroup("Home Screen Player HUD")]
		[SerializeField]
		protected Sprite[] _homeCharacterSprites;

		// Token: 0x04001953 RID: 6483
		[BoxGroup("Home Screen Player HUD")]
		[SerializeField]
		protected TextMeshProUGUI m_levelValueText;

		// Token: 0x04001954 RID: 6484
		[BoxGroup("Home Screen Player HUD")]
		[SerializeField]
		protected TextMeshProUGUI m_experienceText;

		// Token: 0x04001955 RID: 6485
		[BoxGroup("Home Screen Player HUD")]
		[SerializeField]
		protected TextMeshProUGUI m_nextLevelExperienceText;

		// Token: 0x04001956 RID: 6486
		[BoxGroup("Transition")]
		[SerializeField]
		protected RectTransform _transScreenFill;

		// Token: 0x04001957 RID: 6487
		[BoxGroup("Transition")]
		[SerializeField]
		protected RectTransform _transMaskEnd;

		// Token: 0x04001958 RID: 6488
		[BoxGroup("Transition")]
		[SerializeField]
		protected Animator _transitionAnimator;

		// Token: 0x04001959 RID: 6489
		[BoxGroup("Latency Indicator")]
		[SerializeField]
		private Image m_wifiSymbol;

		// Token: 0x0400195A RID: 6490
		[BoxGroup("Latency Indicator")]
		[SerializeField]
		private Sprite[] m_wifiIcons;

		// Token: 0x0400195B RID: 6491
		[SerializeField]
		protected UISelectable m_mapScreenSelectable;

		// Token: 0x0400195C RID: 6492
		[SerializeField]
		protected UISelectable m_honkrScreenSelectable;

		// Token: 0x0400195D RID: 6493
		[SerializeField]
		protected FModAudioMap m_audiomap;

		// Token: 0x0400195E RID: 6494
		[SerializeField]
		public Image m_slideInImage;

		// Token: 0x0400195F RID: 6495
		[SerializeField]
		public UI_Selfie_SubviewBuilder m_slideInImage_selfieBuilder;

		// Token: 0x04001960 RID: 6496
		[SerializeField]
		public Animator m_slideInImageAnimator;

		// Token: 0x04001961 RID: 6497
		[SerializeField]
		public UISelectable[] m_homeButtons;

		// Token: 0x04001962 RID: 6498
		protected UI_CellPhone_Inventory m_inventoryScreen;

		// Token: 0x04001963 RID: 6499
		protected UI_CellPhone_Accessories m_accessoryScreen;

		// Token: 0x04001964 RID: 6500
		protected UI_CellPhone_Snapshot m_snapshotScreen;

		// Token: 0x04001965 RID: 6501
		protected UI_CellPhone_Honkr m_honkrScreen;

		// Token: 0x04001966 RID: 6502
		protected UI_CellPhone_Settings m_settingsScreen;

		// Token: 0x04001967 RID: 6503
		protected UI_CellPhone_MovesList m_movesListScreen;

		// Token: 0x04001968 RID: 6504
		protected UI_CellPhone_Map m_mapScreen;

		// Token: 0x04001969 RID: 6505
		protected UI_Cellphone_Online m_onlineScreen;

		// Token: 0x0400196A RID: 6506
		protected UI_CellPhone_SubScreen m_currentScreen;

		// Token: 0x0400196B RID: 6507
		protected NetworkState m_networkState;

		// Token: 0x0400196C RID: 6508
		protected List<Vector2> _homeButtonPositions = new List<Vector2>();

		// Token: 0x0400196D RID: 6509
		protected Vector3 m_startPosition;

		// Token: 0x0400196E RID: 6510
		protected Vector3 m_startRotation;

		// Token: 0x0400196F RID: 6511
		protected UI_CellPhone_SubScreen m_screenToTransitionTo;

		// Token: 0x04001970 RID: 6512
		protected uint m_contollerNetId;

		// Token: 0x04001971 RID: 6513
		protected uint m_playerNetId;

		// Token: 0x04001972 RID: 6514
		protected bool m_disableInput;

		// Token: 0x04001973 RID: 6515
		protected bool m_setStartPosAndRot;

		// Token: 0x04001974 RID: 6516
		protected List<int> m_viewedHonkrNameHashes = new List<int>();

		// Token: 0x04001975 RID: 6517
		protected string[] m_scenesToShowOnMapScreen;

		// Token: 0x04001976 RID: 6518
		protected bool m_showHonkrOnOpen;

		// Token: 0x04001977 RID: 6519
		protected EventInstance m_snapshot;

		// Token: 0x04001978 RID: 6520
		protected Selectable m_prevSelection;

		// Token: 0x04001979 RID: 6521
		protected string m_phoneUserName;

		// Token: 0x0400197A RID: 6522
		protected bool m_mapScreenShortcut;

		// Token: 0x0400197B RID: 6523
		protected bool m_onlineScreenShortcut;

		// Token: 0x0400197F RID: 6527
		private IRestrictedList<PlayerControllerEntity> remainingEntities;

		// Token: 0x0200041C RID: 1052
		public enum CellphoneTransitions
		{
			// Token: 0x04001981 RID: 6529
			Exit,
			// Token: 0x04001982 RID: 6530
			Inventory,
			// Token: 0x04001983 RID: 6531
			Accessories,
			// Token: 0x04001984 RID: 6532
			Honkr,
			// Token: 0x04001985 RID: 6533
			Movelist,
			// Token: 0x04001986 RID: 6534
			Settings,
			// Token: 0x04001987 RID: 6535
			Navigation,
			// Token: 0x04001988 RID: 6536
			Snapr
		}
	}
}
