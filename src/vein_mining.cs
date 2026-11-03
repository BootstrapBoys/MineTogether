using System;
using Object = UnityEngine.Object;
using Paths = BepInEx.Paths;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace MineTogether
{
	internal static class Catalog
	{
		private static readonly HashSet<string> Ores = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"CopperOre", "TinOre", "IronScrap", "SilverOre", "FlametalOre", "FlametalOreNew", "CopperScrap", "BlackMetalScrap", "Obsidian", "Chitin",
			"Stone", "BlackMarble", "Grausten"
		};

		internal static string PrefabName(GameObject go)
		{
			ZNetView component = go.GetComponent<ZNetView>();
			GameObject val = (((component != null) && component.IsValid() && (ZNetScene.instance != null)) ? ZNetScene.instance.GetPrefab(component.GetZDO().GetPrefab()) : null);
			if (!(val != null))
			{
				return Utils.GetPrefabName(go);
			}
			return ((Object)val).name;
		}

		internal static IEnumerable<string> Drops(GameObject go, int depth = 0)
		{
			List<DropTable.DropData> drops = go.GetComponent<MineRock5>()?.m_dropItems?.m_drops
				?? go.GetComponent<MineRock>()?.m_dropItems?.m_drops
				?? go.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed?.m_drops;
			IEnumerable<string> enumerable = drops == null
				? Enumerable.Empty<string>()
				: from d in drops where d.m_item != null select Utils.GetPrefabName(d.m_item);
			GameObject val = go.GetComponent<Destructible>()?.m_spawnWhenDestroyed;
			if (!(val != null) || depth >= 4)
			{
				return enumerable;
			}
			return enumerable.Concat(Drops(val, depth + 1)).Distinct();
		}

		internal static bool Supported(GameObject go)
		{
			//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e7: Invalid comparison between Unknown and I4
			//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f5: Invalid comparison between Unknown and I4
			if (!(Deposit.ComponentOn(go) != null) || (go.GetComponent<Piece>() != null))
			{
				return false;
			}
			Destructible component = go.GetComponent<Destructible>();
			if ((component != null) && ((component.m_spawnWhenDamaged != null) || ((component.m_spawnWhenDestroyed != null) && !(component.m_spawnWhenDestroyed.GetComponent<MineRock5>() != null))))
			{
				return false;
			}
			string name = PrefabName(go);
			if (Plugin.Instance.Excluded.Value.Split(new char[1] { ',' }).Any((string x) => x.Trim() == name))
			{
				return false;
			}
			if (Plugin.Instance.Included.Value.Split(new char[1] { ',' }).Any((string x) => x.Trim() == name))
			{
				return true;
			}
			if ((component != null) && ((int)component.m_damages.m_pickaxe == 3 || (int)component.m_damages.m_chop != 3 || (go.GetComponent<Character>() != null)))
			{
				return false;
			}
			return Drops(go).Any((string d) => Ores.Contains(d) || d.EndsWith("Ore", StringComparison.OrdinalIgnoreCase));
		}

		internal static int RequiredTier(GameObject go)
		{
			Destructible component = go.GetComponent<Destructible>();
			int num = go.GetComponent<MineRock5>()?.m_minToolTier ?? go.GetComponent<MineRock>()?.m_minToolTier ?? component?.m_minToolTier ?? 0;
			if (!(component != null) || !(component.m_spawnWhenDestroyed != null))
			{
				return num;
			}
			return Math.Max(num, RequiredTier(component.m_spawnWhenDestroyed));
		}

		internal static void Audit()
		{
			if (ZNetScene.instance == null || ZNetScene.instance.m_prefabs == null)
			{
				return;
			}
			List<string> list = new List<string> { "prefab\tadapter\tenabled\tdrops\tstaged\tonDamaged\tonDestroyed\thealth\tpickaxe\tchop" };
			foreach (GameObject item in from g in ZNetScene.instance.m_prefabs
				where (g != null) && (Deposit.ComponentOn(g) != null)
				orderby ((Object)g).name
				select g)
			{
				Destructible component = item.GetComponent<Destructible>();
				list.Add(string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}", ((Object)item).name, ((object)Deposit.ComponentOn(item)).GetType().Name, Supported(item), string.Join(",", Drops(item)), (component != null) && ((component.m_spawnWhenDestroyed != null) || (component.m_spawnWhenDamaged != null)), ((component != null) && (component.m_spawnWhenDamaged != null)) ? ((Object)component.m_spawnWhenDamaged).name : "", ((component != null) && (component.m_spawnWhenDestroyed != null)) ? ((Object)component.m_spawnWhenDestroyed).name : "", (component != null) ? component.m_health : 0f, (component != null) ? ((HitData.DamageModifier)component.m_damages.m_pickaxe).ToString() : "", (component != null) ? ((HitData.DamageModifier)component.m_damages.m_chop).ToString() : ""));
			}
			string text = Path.Combine(Paths.ConfigPath, "MineTogether.prefabs.tsv");
			File.WriteAllLines(text, list);
			Plugin.Instance.Log.LogInfo((object)$"Prefab audit written: {text}; {list.Count - 1} destructible prefabs examined.");
		}
	}
	internal static class Client
	{
		internal static ZDOID Target = ZDOID.None;

		internal static bool SwingPending;

		private static long token;

		private static int sequence;

		private static float requestAt;

		private static float heartbeatAt;

		private static float missingSince;

		private static bool pending;

		private static float pauseCheckAt;

		private static ItemDrop.ItemData? tool;

		private static Attack? attack;

		private static readonly List<(int index, HitData hit)> Batch = new List<(int, HitData)>();

		private static ZDOID cachedTarget = ZDOID.None;

		private static ZNetView? cachedView;


		internal static bool Running => token != 0;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static bool IsHolding()
		{
			// Always-on: "holding" now means the feature is enabled, not that a key is down.
			return Switches.VeinMine;
		}

		internal static void Reset()
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			Target = ZDOID.None;
			token = 0L;
			sequence = 0;
			pending = false;
			tool = null;
			attack = null;
			missingSince = 0f;
			cachedTarget = ZDOID.None;
			cachedView = null;
			Batch.Clear();
		}

		private static void Message(string text)
		{
			if ((MessageHud.instance != null))
			{
				MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text, 0, (Sprite)null, false, true);
			}
		}

		internal static void Stop(string message, bool keepTap = false)
		{
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Expected O, but got Unknown
			if (token != 0L)
			{
				Diag.SessionsStopped++;
				Diag.Trace("[Vein]", message.Length > 0 ? "session stopped: " + message : "session stopped");
			}
			if (token != 0L && ZRoutedRpc.instance != null)
			{
				ZPackage val = new ZPackage();
				val.Write(token);
				ZRoutedRpc.instance.InvokeRoutedRPC(Protocol.Stop, new object[1] { val });
			}
			Reset();
			if (message.Length > 0)
			{
				Message(message);
			}
		}

		internal static void Update()
		{
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			//IL_018f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0196: Expected O, but got Unknown
			//IL_0212: Unknown result type (might be due to invalid IL or missing references)
			//IL_0219: Invalid comparison between Unknown and I4
			//IL_025c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0267: Unknown result type (might be due to invalid IL or missing references)
			//IL_031f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0326: Unknown result type (might be due to invalid IL or missing references)
			//IL_0349: Unknown result type (might be due to invalid IL or missing references)
			//IL_034e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0363: Unknown result type (might be due to invalid IL or missing references)
			//IL_036a: Expected O, but got Unknown
			//IL_0374: Unknown result type (might be due to invalid IL or missing references)
			//IL_0381: Unknown result type (might be due to invalid IL or missing references)
			Diag.TraceOnce("tick:client", "[Tick]", "client update running");
			Player localPlayer = Player.m_localPlayer;
			if (!(localPlayer != null) || ZRoutedRpc.instance == null)
			{
				Reset();
				return;
			}
			// Vein mining is always active; the hotkey toggles it. The choice is written back so
			// it survives a restart.
			if (Application.isFocused && !InputBlocked() && Plugin.Instance.ToggleKey.Value.IsDown())
			{
				Diag.Trace("[Vein]", $"toggle pressed: master={Switches.Master} vein={Plugin.Instance.VeinMine.Value}");
				if (!Switches.Master)
				{
					Message("Mine Together is disabled.");
					return;
				}
				bool nowEnabled = !Plugin.Instance.VeinMine.Value;
				Plugin.Instance.VeinMine.Value = nowEnabled;
				Plugin.Instance.Config.Save();
				if (!nowEnabled)
				{
					Stop("Mine Together: vein mining OFF");
				}
				else
				{
					Message("Mine Together: vein mining ON");
				}
				return;
			}
			bool flag = Switches.VeinMine;
			if ((Running || pending) && !flag)
			{
				Stop("Vein mining off.");
				return;
			}
			if (pending && Time.time - requestAt > 8f)
			{
				Stop("The server did not respond. Check that Mine Together is installed on it.");
			}
			if (Running && (!ValidPlayer(localPlayer) || ((Humanoid)localPlayer).GetCurrentWeapon() != tool || (tool.m_shared.m_useDurability && tool.m_durability <= 0f)))
			{
				Stop("Mining stopped. Shared progress has been saved.");
			}
			// Distance to the deposit's nearest exposed collider, not its ZDO origin: a large rock
			// formation has its centre far from where the player stands, so an origin-based check
			// paused sessions on the same swing that started them (seen on rock4_forest_frac).
			if (Running && Time.time >= pauseCheckAt)
			{
				// Four times a second is plenty for a walk-away check and keeps the per-area collider
				// maths off the frame budget of a large deposit.
				pauseCheckAt = Time.time + 0.25f;
				Deposit? live = Deposit.Find(Target);
				Vector3 here = ((Component)localPlayer).transform.position;
				float gap = live != null
					? live.Distance(here)
					: (TargetView() is ZNetView watched && watched.GetZDO() != null
						? Vector3.Distance(here, watched.transform.position)
						: 0f);
				if (live != null && gap > Plugin.Instance.Range.Value + 2f)
				{
					Stop("Mining paused.");
				}
			}
			if (Running && (Object)TargetView() == (Object)null)
			{
				if (missingSince == 0f)
				{
					missingSince = Time.time;
				}
				if (Time.time - missingSince > 2f)
				{
					Stop("");
				}
			}
			else
			{
				missingSince = 0f;
			}
			if (Running && Time.time >= heartbeatAt)
			{
				heartbeatAt = Time.time + 3f;
				ZPackage val = new ZPackage();
				val.Write(token);
				ZRoutedRpc.instance.InvokeRoutedRPC(Protocol.Keep, new object[1] { val });
			}
			if (!flag || InputBlocked() || Running || pending)
			{
				SwingPending = false;
				return;
			}
			// Start on a swing, never on a timer. With vein mining always on, this block used to
			// run every frame and each failed check printed its message once per frame.
			if (!SwingPending)
			{
				return;
			}
			SwingPending = false;
			if (!MiningCompatibility.Ready)
			{
				Message("This version of Smoothbrain Mining is not supported.");
				return;
			}
			tool = ((Humanoid)localPlayer).GetCurrentWeapon();
			if (tool == null || (int)tool.m_shared.m_skillType != 12)
			{
				// Swung with something else: not a mining attempt, and not worth a message.
				return;
			}
			if (!ValidPlayer(localPlayer) || (tool.m_shared.m_useDurability && tool.m_durability <= 0f))
			{
				Message("That pickaxe needs repairing first.");
				return;
			}
			Camera mainCamera = Utils.GetMainCamera();
			RaycastHit val2 = default(RaycastHit);
			if (!(mainCamera != null) || !Physics.Raycast(((Component)mainCamera).transform.position, ((Component)mainCamera).transform.forward, out val2, 50f, LayerMask.GetMask(new string[5] { "Default", "static_solid", "Default_small", "piece", "terrain" }), (QueryTriggerInteraction)1))
			{
				return;
			}
			ZNetView componentInParent = ((Component)val2.collider).GetComponentInParent<ZNetView>();
			if (!(componentInParent != null) || !componentInParent.IsValid() || !(Deposit.ComponentOn(((Component)componentInParent).gameObject) != null))
			{
				return;
			}
			int num2 = Catalog.RequiredTier(((Component)componentInParent).gameObject);
			if (tool.m_shared.m_toolTier < num2)
			{
				Message("Your pickaxe is too weak for this deposit.");
				return;
			}
			if (Vector3.Distance(((Component)localPlayer).transform.position, val2.point) > 6f)
			{
				Message("Move closer to the deposit.");
				return;
			}
			Target = componentInParent.GetZDO().m_uid;
			pending = true;
			requestAt = Time.time;
			ZPackage val3 = new ZPackage();
			val3.Write(1);
			val3.Write(Target);
			val3.Write(((Character)localPlayer).GetZDOID());
			ZRoutedRpc.instance.InvokeRoutedRPC(Protocol.Start, new object[1] { val3 });
			Diag.SessionsStarted++;
			Diag.Trace("[Vein]", $"start requested on {Catalog.PrefabName(componentInParent.gameObject)} (tier {num2})");
		}

		private static bool ValidPlayer(Player player)
		{
			if (!((Character)player).IsDead() && !((Character)player).IsTeleporting() && !((Character)player).InCutscene())
			{
				return !((Character)player).InPlaceMode();
			}
			return false;
		}

		private static bool InputBlocked()
		{
			if ((!(Chat.instance != null) || !Chat.instance.HasFocus()) && !Console.IsVisible() && !TextInput.IsVisible() && !InventoryGui.IsVisible())
			{
				return Menu.IsVisible();
			}
			return true;
		}

		private static ZNetView? TargetView()
		{
			// Called twice per frame while mining; the ZDOID only changes on a new target, and
			// IsValid() is what makes a cached hit safe when the deposit is destroyed.
			if (cachedTarget == Target && cachedView != null && cachedView.IsValid())
			{
				return cachedView;
			}
			GameObject val = ((ZNetScene.instance != null) ? ZNetScene.instance.FindInstance(Target) : null);
			ZNetView val2 = ((val != null) ? val.GetComponent<ZNetView>() : null);
			if (!(val2 != null) || !val2.IsValid())
			{
				cachedTarget = ZDOID.None;
				cachedView = null;
				return null;
			}
			cachedTarget = Target;
			cachedView = val2;
			return val2;
		}

		internal static void Reply(long sender, ZPackage pkg)
		{
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			if (sender != Server.ServerId)
			{
				return;
			}
			try
			{
				if (pkg.GetArray().Length > 2048)
				{
					return;
				}
				ZDOID val = pkg.ReadZDOID();
				long num = pkg.ReadLong();
				string text = pkg.ReadString();
				if (!(val != Target) && (pending || Running))
				{
					pending = false;
					if (num == 0L)
					{
						Reset();
					}
					else
					{
						token = num;
						sequence = 0;
					}
					Message(text);
				}
			}
			catch (Exception ex)
			{
				Plugin.Instance.Log.LogWarning((object)ex.Message);
			}
		}

		internal static void Retarget(long sender, ZPackage pkg)
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_003c: Unknown result type (might be due to invalid IL or missing references)
			if (sender != Server.ServerId)
			{
				return;
			}
			try
			{
				ZDOID val = pkg.ReadZDOID();
				ZDOID target = pkg.ReadZDOID();
				long num = pkg.ReadLong();
				if (Running && Target == val && token == num)
				{
					Target = target;
					missingSince = 0f;
				}
			}
			catch (Exception ex)
			{
				Plugin.Instance.Log.LogDebug((object)ex.Message);
			}
		}

		

		internal static void Begin(Attack current, Humanoid character)
		{
			if (Running && !((Object)character != (Object)Player.m_localPlayer))
			{
				attack = current;
				Batch.Clear();
			}
		}

		internal static bool Capture(Component rock, HitData hit)
		{
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Unknown result type (might be due to invalid IL or missing references)
			//IL_01be: Unknown result type (might be due to invalid IL or missing references)
			//IL_01c5: Expected O, but got Unknown
			//IL_01cd: Unknown result type (might be due to invalid IL or missing references)
			ZNetView component = rock.GetComponent<ZNetView>();
			if (!(component != null) || !component.IsValid())
			{
				return true;
			}
			if (attack != null && Running && component.GetZDO().m_uid == Target && (Object)hit.GetAttacker() == (Object)Player.m_localPlayer)
			{
				MineRock5 val = (MineRock5)(object)((rock is MineRock5) ? rock : null);
				int num;
				if (val != null)
				{
					num = (int)GameRefs.MineRock5GetAreaIndex.Invoke(val, new object[1] { hit.m_hitCollider });
				}
				else
				{
					MineRock val2 = (MineRock)(object)((rock is MineRock) ? rock : null);
					num = ((val2 != null) ? Array.IndexOf((Collider[])GameRefs.MineRockHitAreas.GetValue(val2), hit.m_hitCollider) : 0);
				}
				if (num >= 0 && Batch.Count < 64)
				{
					Batch.Add((num, hit.Clone()));
				}
				return false;
			}
			if (component.GetZDO().GetBool(Protocol.Active, false) && (Object)hit.GetAttacker() == (Object)Player.m_localPlayer && hit.m_damage.m_pickaxe > 0f)
			{
				MineRock5 val3 = (MineRock5)(object)((rock is MineRock5) ? rock : null);
				int num2;
				if (val3 == null)
				{
					MineRock val4 = (MineRock)(object)((rock is MineRock) ? rock : null);
					num2 = ((val4 != null) ? Array.IndexOf((Collider[])GameRefs.MineRockHitAreas.GetValue(val4), hit.m_hitCollider) : 0);
				}
				else
				{
					num2 = (int)GameRefs.MineRock5GetAreaIndex.Invoke(val3, new object[1] { hit.m_hitCollider });
				}
				int num3 = num2;
				if (num3 < 0)
				{
					return true;
				}
				ZPackage val5 = new ZPackage();
				val5.Write(component.GetZDO().m_uid);
				val5.Write(num3);
				hit.Serialize(ref val5);
				ZRoutedRpc.instance.InvokeRoutedRPC(Protocol.Manual, new object[1] { val5 });
				return false;
			}
			return true;
		}

		internal static void End(Attack current, Exception? error)
		{
			//IL_004a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0050: Expected O, but got Unknown
			if (attack != current)
			{
				return;
			}
			attack = null;
			if (error != null)
			{
				Batch.Clear();
				Stop("Mining stopped because of an error.");
				return;
			}
			if (Batch.Count == 0)
			{
				Stop("Your pickaxe missed the deposit. Adjust your aim and swing again.");
				return;
			}
			if (Running)
			{
				ZPackage val = new ZPackage();
				val.Write(token);
				val.Write(++sequence);
				val.Write(Batch.Count);
				foreach (var item in Batch)
				{
					val.Write(item.index);
					item.hit.Serialize(ref val);
				}
				ZRoutedRpc.instance.InvokeRoutedRPC(Protocol.Hit, new object[1] { val });
			}
			Batch.Clear();
		}

		internal static void Draw()
		{
			Player localPlayer = Player.m_localPlayer;
			if (!(localPlayer != null))
			{
				MiningHud.Hide();
				return;
			}
			ZNetView val = TargetView();
			if (!(val != null))
			{
				GameObject hoverObject = ((Humanoid)localPlayer).GetHoverObject();
				val = ((hoverObject != null) ? hoverObject.GetComponentInParent<ZNetView>() : null);
			}
			if (!(val != null) || !val.IsValid() || !val.GetZDO().GetBool(Protocol.Active, false))
			{
				MiningHud.Hide();
				return;
			}
			float progress = Mathf.Clamp01(val.GetZDO().GetFloat(Protocol.Progress, 0f));
			ZDO zDO = val.GetZDO();
			int miners = (((ZNet.instance != null) && ZNet.instance.GetTimeSeconds() - (double)zDO.GetLong(Protocol.ActivityTimeKey, 0L) < 6.0) ? zDO.GetInt(Protocol.MinersKey, 0) : 0);
			float num = zDO.GetFloat(Protocol.SpeedKey, 0f);
			if (Server.IsServer)
			{
				num = Server.SpeedRatio(zDO.m_uid);
			}
			else if (num <= 0f)
			{
				num = 1f;
			}
			MiningHud.Draw(progress, miners, num, Switches.VeinMine && Running);
		}
	}
	
	[HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
	internal static class SwingBatch
	{
		private static void Prefix(Attack __instance, Humanoid ___m_character)
		{
			Diag.TraceOnce("tick:swing", "[Swing]", "melee swing observed");
			if (___m_character == Player.m_localPlayer) Client.SwingPending = true;
			Client.Begin(__instance, ___m_character);
		}

		private static void Finalizer(Attack __instance, Exception __exception)
		{
			Client.End(__instance, __exception);
		}
	}
	[HarmonyPatch]
	internal static class CaptureHit
	{
		[CompilerGenerated]
		private sealed class TargetMethodsEnum : IEnumerable<MethodBase>, IEnumerable, IEnumerator<MethodBase>, IDisposable, IEnumerator
		{
			private int _state;

			private MethodBase _current;

			private int _initialThreadId;

			MethodBase IEnumerator<MethodBase>.Current
			{
				[DebuggerHidden]
				get
				{
					return _current;
				}
			}

			object IEnumerator.Current
			{
				[DebuggerHidden]
				get
				{
					return _current;
				}
			}

			[DebuggerHidden]
			public TargetMethodsEnum(int _state)
			{
				this._state = _state;
				_initialThreadId = Environment.CurrentManagedThreadId;
			}

			[DebuggerHidden]
			void IDisposable.Dispose()
			{
				_state = -2;
			}

			private bool MoveNext()
			{
				switch (_state)
				{
				default:
					return false;
				case 0:
					_state = -1;
					_current = GameRefs.MineRock5Damage;
					_state = 1;
					return true;
				case 1:
					_state = -1;
					_current = GameRefs.MineRockDamage;
					_state = 2;
					return true;
				case 2:
					_state = -1;
					_current = GameRefs.DestructibleDamage;
					_state = 3;
					return true;
				case 3:
					_state = -1;
					return false;
				}
			}

			bool IEnumerator.MoveNext()
			{
				//ILSpy generated this explicit interface implementation from .override directive in MoveNext
				return this.MoveNext();
			}

			[DebuggerHidden]
			void IEnumerator.Reset()
			{
				throw new NotSupportedException();
			}

			[DebuggerHidden]
			IEnumerator<MethodBase> IEnumerable<MethodBase>.GetEnumerator()
			{
				if (_state == -2 && _initialThreadId == Environment.CurrentManagedThreadId)
				{
					_state = 0;
					return this;
				}
				return new TargetMethodsEnum(0);
			}

			[DebuggerHidden]
			IEnumerator IEnumerable.GetEnumerator()
			{
				return ((IEnumerable<MethodBase>)this).GetEnumerator();
			}
		}

		[IteratorStateMachine(typeof(TargetMethodsEnum))]
		private static IEnumerable<MethodBase> TargetMethods()
		{
			//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
			return new TargetMethodsEnum(-2);
		}

		[HarmonyPriority(800)]
		private static bool Prefix(Component __instance, HitData hit)
		{
			if (!Plugin.Completing)
			{
				return Client.Capture(__instance, hit);
			}
			return true;
		}
	}
	internal sealed class Deposit
	{
		internal readonly Component Rock;

		internal readonly ZNetView View;

		internal readonly Collider[] Parts;

		internal readonly float[] Health;

		internal readonly int Tier;

		internal readonly HitData.DamageModifiers Resistance;

		internal readonly MineRock5? nextStage;

		internal static readonly List<ZNetView> Spawned = new List<ZNetView>();

		internal ZDO Data => View.GetZDO();

		internal string Name => Catalog.PrefabName(Rock.gameObject);

		internal Deposit(Component rock)
		{
			//IL_01a5: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ab: Expected O, but got Unknown
			//IL_0215: Unknown result type (might be due to invalid IL or missing references)
			//IL_021a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0199: Unknown result type (might be due to invalid IL or missing references)
			//IL_019e: Unknown result type (might be due to invalid IL or missing references)
			//IL_010e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0113: Unknown result type (might be due to invalid IL or missing references)
			Deposit deposit = this;
			Rock = rock;
			View = rock.GetComponent<ZNetView>();
			if (!(View != null) || !View.IsValid())
			{
				throw new InvalidOperationException("Deposit is not networked");
			}
			MineRock5 val = (MineRock5)(object)((rock is MineRock5) ? rock : null);
			if (val != null)
			{
				GameRefs.MineRock5LoadHealth.Invoke(val, null);
				IList source = (IList)GameRefs.MineRock5HitAreas.GetValue(val);
				Parts = source.Cast<object>().Select((Func<object, Collider>)((object a) => (Collider)GameRefs.HitAreaCollider.GetValue(a))).ToArray();
				Health = (from object a in source
					select Math.Max(0f, (float)GameRefs.HitAreaHealth.GetValue(a))).ToArray();
				Tier = val.m_minToolTier;
				Resistance = val.m_damageModifiers;
				return;
			}
			MineRock old = (MineRock)(object)((rock is MineRock) ? rock : null);
			if (old != null)
			{
				Parts = (Collider[])GameRefs.MineRockHitAreas.GetValue(old);
				Health = (from i in Enumerable.Range(0, Parts.Length)
					select Math.Max(0f, deposit.Data.GetFloat("Health" + i, old.GetHealth()))).ToArray();
				Tier = old.m_minToolTier;
				Resistance = old.m_damageModifiers;
				return;
			}
			Destructible val2 = (Destructible)rock;
			Parts = (Collider[])(object)new Collider[1] { rock.GetComponentInChildren<Collider>() };
			Health = new float[1] { Math.Max(0f, Data.GetFloat(ZDOVars.s_health, val2.m_health * (1f + (float)Game.m_worldLevel * Game.instance.m_worldLevelMineHPMultiplier))) };
			Tier = val2.m_minToolTier;
			Resistance = val2.m_damages;
			nextStage = ((val2.m_spawnWhenDestroyed != null) ? val2.m_spawnWhenDestroyed.GetComponent<MineRock5>() : null);
			if ((nextStage != null))
			{
				int count = ((Component)nextStage).GetComponentsInChildren<Collider>().Length;
				float element = nextStage.m_health * (1f + (float)Game.m_worldLevel * Game.instance.m_worldLevelMineHPMultiplier);
				Health = Health.Concat(Enumerable.Repeat(element, count)).ToArray();
				Tier = Math.Max(Tier, nextStage.m_minToolTier);
			}
		}

		internal static Component? ComponentOn(GameObject go)
		{
			return (Component?)(go.GetComponent<MineRock5>() ?? ((object)go.GetComponent<MineRock>()) ?? ((object)go.GetComponent<Destructible>()));
		}

		internal static Deposit? Find(ZDOID id)
		{
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			GameObject val = ((ZNetScene.instance != null) ? ZNetScene.instance.FindInstance(id) : null);
			if (!(val != null) || !Catalog.Supported(val))
			{
				return null;
			}
			Component val2 = ComponentOn(val);
			if (!(val2 != null))
			{
				return null;
			}
			return new Deposit(val2);
		}

		internal HitData.DamageModifiers ResistanceAt(int index)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			if (!(nextStage != null) || index <= 0)
			{
				return Resistance;
			}
			return nextStage.m_damageModifiers;
		}

		internal float Distance(Vector3 position)
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			return Parts.Where((Collider c) => (c != null) && c.enabled).Select(delegate(Collider c)
			{
				//IL_0014: Unknown result type (might be due to invalid IL or missing references)
				//IL_0019: Unknown result type (might be due to invalid IL or missing references)
				//IL_0035: Unknown result type (might be due to invalid IL or missing references)
				//IL_0021: Unknown result type (might be due to invalid IL or missing references)
				//IL_0026: Unknown result type (might be due to invalid IL or missing references)
				//IL_002a: Unknown result type (might be due to invalid IL or missing references)
				//IL_002f: Unknown result type (might be due to invalid IL or missing references)
				MeshCollider val = (MeshCollider)(object)((c is MeshCollider) ? c : null);
				Vector3 val2;
				if (val == null || val.convex)
				{
					val2 = c.ClosestPoint(position);
				}
				else
				{
					Bounds bounds = c.bounds;
					val2 = bounds.ClosestPoint(position);
				}
				return Vector3.Distance(val2, position);
			}).DefaultIfEmpty(float.MaxValue)
				.Min();
		}

		internal WorkLedger Load()
		{
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_0037: Expected O, but got Unknown
			byte[] byteArray = Data.GetByteArray(Protocol.Ledger, (byte[])null);
			if (byteArray == null || byteArray.Length == 0)
			{
				return new WorkLedger(Health, Health.Sum());
			}
			ZPackage val = new ZPackage(byteArray);
			if (val.ReadInt() != 1 || val.ReadInt() != Health.Length)
			{
				throw new InvalidOperationException("Deposit layout changed; refusing stale progress");
			}
			float total = val.ReadSingle();
			float[] array = new float[Health.Length];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = val.ReadSingle();
			}
			return new WorkLedger(array, total);
		}

		internal void Save(WorkLedger ledger)
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Expected O, but got Unknown
			ZPackage val = new ZPackage();
			val.Write(1);
			val.Write(ledger.Remaining.Length);
			val.Write(ledger.Total);
			float[] remaining = ledger.Remaining;
			foreach (float num in remaining)
			{
				val.Write(num);
			}
			Data.Set(Protocol.Ledger, val.GetArray());
			Data.Set(Protocol.Progress, ledger.Progress);
		}

		internal void Finish(HitData source)
		{
			//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
			//IL_0113: Unknown result type (might be due to invalid IL or missing references)
			//IL_0118: Unknown result type (might be due to invalid IL or missing references)
			//IL_011c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0103: Unknown result type (might be due to invalid IL or missing references)
			//IL_0121: Unknown result type (might be due to invalid IL or missing references)
			bool completing = Plugin.Completing;
			Plugin.Completing = true;
			try
			{
				string expectedStage = ((nextStage != null) ? Utils.GetPrefabName(((Component)nextStage).gameObject) : null);
				float[] array = (from i in Enumerable.Range(0, Health.Length)
					select Data.GetFloat(Protocol.FactorPrefix + i, 0f)).ToArray();
				int count = Spawned.Count;
				for (int j = 0; j < Parts.Length; j++)
				{
					if (!(Rock != null))
					{
						break;
					}
					if (!View.IsValid())
					{
						break;
					}
					if (Health[j] <= 0f)
					{
						continue;
					}
					using (MiningCompatibility.Scope(Data.GetFloat(Protocol.FactorPrefix + j, 0f)))
					{
						HitData val = source.Clone();
						val.m_damage = new HitData.DamageTypes
						{
							m_damage = Health[j] + 1f
						};
						val.m_toolTier = short.MaxValue;
						Vector3 point;
						if (!(Parts[j] != null))
						{
							point = Rock.transform.position;
						}
						else
						{
							Bounds bounds = Parts[j].bounds;
							point = bounds.center;
						}
						val.m_point = point;
						val.m_hitCollider = Parts[j];
						Component rock = Rock;
						MineRock5 val2 = (MineRock5)(object)((rock is MineRock5) ? rock : null);
						if (val2 != null)
						{
							GameRefs.MineRock5DamageArea.Invoke(val2, new object[2] { j, val });
							continue;
						}
						Component rock2 = Rock;
						MineRock val3 = (MineRock)(object)((rock2 is MineRock) ? rock2 : null);
						if (val3 != null)
						{
							GameRefs.MineRockRpcHit.Invoke(val3, new object[3] { 0L, val, j });
						}
						else
						{
							GameRefs.DestructibleRpcDamage.Invoke(Rock, new object[2] { 0L, val });
						}
					}
				}
				if (expectedStage != null)
				{
					ZNetView? obj = Spawned.Skip(count).FirstOrDefault((Func<ZNetView, bool>)((ZNetView v) => (v != null) && v.IsValid() && v.GetZDO().GetPrefab() == StringExtensionMethods.GetStableHashCode(expectedStage)));
					if (!(obj != null))
					{
						throw new InvalidOperationException("Native ore transition did not create " + expectedStage);
					}
					Deposit deposit = new Deposit((Component)(object)((Component)obj).GetComponent<MineRock5>());
					for (int k = 0; k < deposit.Health.Length; k++)
					{
						deposit.Data.Set(Protocol.FactorPrefix + k, array[k + 1]);
					}
					deposit.Finish(source);
				}
				if ((Rock != null) && View.IsValid())
				{
					Data.Set(Protocol.Active, false);
				}
			}
			finally
			{
				Plugin.Completing = completing;
				if (!completing)
				{
					Spawned.Clear();
				}
			}
		}
	}
	[HarmonyPatch(typeof(ZNetScene), "AddInstance")]
	internal static class CaptureNativeStage
	{
		private static void Postfix(ZNetView nview)
		{
			if (Plugin.Completing)
			{
				Deposit.Spawned.Add(nview);
			}
			ManualMining.RecordSpawn(nview);
		}
	}
	internal static class ManualMining
	{
		internal sealed class Snapshot
		{
			internal Deposit Deposit;

			internal ZDOID Id;

			internal WorkLedger Ledger;

			internal HitData Hit;

			internal float[] Factors = Array.Empty<float>();

			internal readonly List<(ZDOID id, int prefab)> Spawned = new List<(ZDOID, int)>();
		}

		private static readonly List<Snapshot> Active = new List<Snapshot>();

		internal static void Reset()
		{
			Active.Clear();
		}

		internal static Snapshot? Begin(Component rock, HitData hit)
		{
			//IL_009c: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
			Component rock2 = rock;
			if (Plugin.Completing || !Server.IsServer || Active.Any((Snapshot s) => (Object)s.Deposit.Rock == (Object)rock2))
			{
				return null;
			}
			ZNetView component = rock2.GetComponent<ZNetView>();
			if (!(component != null) || !component.IsValid() || !component.IsOwner() || !component.GetZDO().GetBool(Protocol.Active, false))
			{
				return null;
			}
			Deposit d = new Deposit(rock2);
			Snapshot snapshot = new Snapshot
			{
				Deposit = d,
				Id = d.Data.m_uid,
				Ledger = d.Load(),
				Hit = hit.Clone(),
				Factors = (from i in Enumerable.Range(0, d.Health.Length)
					select d.Data.GetFloat(Protocol.FactorPrefix + i, 0f)).ToArray()
			};
			Active.Add(snapshot);
			return snapshot;
		}

		internal static void RecordSpawn(ZNetView view)
		{
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			if (Plugin.Completing || Active.Count == 0 || !(view != null) || !view.IsValid())
			{
				return;
			}
			foreach (Snapshot item in Active)
			{
				item.Spawned.Add((view.GetZDO().m_uid, view.GetZDO().GetPrefab()));
			}
		}

		internal static void End(Snapshot? s)
		{
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_016c: Unknown result type (might be due to invalid IL or missing references)
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0092: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
			//IL_0201: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_0153: Unknown result type (might be due to invalid IL or missing references)
			//IL_015e: Unknown result type (might be due to invalid IL or missing references)
			if (s == null)
			{
				return;
			}
			Active.Remove(s);
			try
			{
				Deposit deposit = Deposit.Find(s.Id);
				WorkLedger workLedger = s.Ledger;
				float[] array = s.Deposit.Health;
				int num = 0;
				if (deposit == null && (s.Deposit.nextStage != null))
				{
					int expected = StringExtensionMethods.GetStableHashCode(((Object)((Component)s.Deposit.nextStage).gameObject).name);
					(ZDOID id, int prefab) tuple = s.Spawned.FirstOrDefault(((ZDOID id, int prefab) x) => x.prefab == expected);
					if (tuple.id == ZDOID.None)
					{
						throw new InvalidOperationException("Native shell transition was not captured");
					}
					deposit = Deposit.Find(tuple.id);
					if (deposit == null)
					{
						Server.Completed(s.Id);
						return;
					}
					workLedger = new WorkLedger(workLedger.Remaining.Skip(1).ToArray(), workLedger.Total);
					array = array.Skip(1).ToArray();
					num = 1;
					deposit.Data.SetOwner(Server.ServerId);
					deposit.Data.Set(Protocol.Active, true);
					for (int i = 0; i < deposit.Health.Length; i++)
					{
						deposit.Data.Set(Protocol.FactorPrefix + i, s.Factors[i + num]);
					}
					Server.Retarget(s.Id, deposit.Data.m_uid);
				}
				if (deposit == null)
				{
					Server.Completed(s.Id);
					return;
				}
				workLedger.ReconcileNative(array, deposit.Health);
				Character attacker = s.Hit.GetAttacker();
				Player val = (Player)(object)((attacker is Player) ? attacker : null);
				if ((val != null))
				{
					for (int j = 0; j < deposit.Health.Length; j++)
					{
						if (deposit.Health[j] < array[j])
						{
							deposit.Data.Set(Protocol.FactorPrefix + j, MiningCompatibility.Factor(val));
						}
					}
				}
				deposit.Save(workLedger);
				if (workLedger.Complete)
				{
					// Completing through ordinary damage means the game has already broken the pieces
					// itself. Finishing here would destroy everything still standing, which is how a
					// single AOE hit used to pop an entire vein.
					deposit.Data.Set(Protocol.Active, false);
					Server.Completed(deposit.Data.m_uid);
				}
			}
			catch (Exception ex)
			{
				Plugin.Instance.Log.LogError((object)("Could not reconcile ordinary mining: " + ex));
			}
		}
	}
	[HarmonyPatch]
	internal static class ObserveNativeMining
	{
		[CompilerGenerated]
		private sealed class TargetMethodsEnum : IEnumerable<MethodBase>, IEnumerable, IEnumerator<MethodBase>, IDisposable, IEnumerator
		{
			private int _state;

			private MethodBase _current;

			private int _initialThreadId;

			MethodBase IEnumerator<MethodBase>.Current
			{
				[DebuggerHidden]
				get
				{
					return _current;
				}
			}

			object IEnumerator.Current
			{
				[DebuggerHidden]
				get
				{
					return _current;
				}
			}

			[DebuggerHidden]
			public TargetMethodsEnum(int _state)
			{
				this._state = _state;
				_initialThreadId = Environment.CurrentManagedThreadId;
			}

			[DebuggerHidden]
			void IDisposable.Dispose()
			{
				_state = -2;
			}

			private bool MoveNext()
			{
				switch (_state)
				{
				default:
					return false;
				case 0:
					_state = -1;
					_current = GameRefs.MineRock5RpcDamage;
					_state = 1;
					return true;
				case 1:
					_state = -1;
					_current = GameRefs.MineRockRpcHit;
					_state = 2;
					return true;
				case 2:
					_state = -1;
					_current = GameRefs.DestructibleRpcDamage;
					_state = 3;
					return true;
				case 3:
					_state = -1;
					return false;
				}
			}

			bool IEnumerator.MoveNext()
			{
				//ILSpy generated this explicit interface implementation from .override directive in MoveNext
				return this.MoveNext();
			}

			[DebuggerHidden]
			void IEnumerator.Reset()
			{
				throw new NotSupportedException();
			}

			[DebuggerHidden]
			IEnumerator<MethodBase> IEnumerable<MethodBase>.GetEnumerator()
			{
				if (_state == -2 && _initialThreadId == Environment.CurrentManagedThreadId)
				{
					_state = 0;
					return this;
				}
				return new TargetMethodsEnum(0);
			}

			[DebuggerHidden]
			IEnumerator IEnumerable.GetEnumerator()
			{
				return ((IEnumerable<MethodBase>)this).GetEnumerator();
			}
		}

		[IteratorStateMachine(typeof(TargetMethodsEnum))]
		private static IEnumerable<MethodBase> TargetMethods()
		{
			//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
			return new TargetMethodsEnum(-2);
		}

		[HarmonyPriority(800)]
		private static void Prefix(Component __instance, HitData hit, out ManualMining.Snapshot? __state)
		{
			__state = null;
			try
			{
				__state = ManualMining.Begin(__instance, hit);
			}
			catch (Exception ex)
			{
				Plugin.Instance.Log.LogError((object)("Could not snapshot ordinary mining: " + ex));
			}
		}

		private static void Finalizer(ManualMining.Snapshot? __state)
		{
			ManualMining.End(__state);
		}
	}
	internal static class MiningCompatibility
	{
		private sealed class MiningScope : IDisposable
		{
			private readonly object? oldMining;

			private readonly object? oldFactor;

			internal MiningScope(float value)
			{
				oldMining = mining?.GetValue(null);
				oldFactor = factor?.GetValue(null);
				mining?.SetValue(null, true);
				factor?.SetValue(null, value);
			}

			public void Dispose()
			{
				mining?.SetValue(null, oldMining);
				factor?.SetValue(null, oldFactor);
			}
		}

		private static Type? flag;

		private static MethodInfo? handle;

		private static FieldInfo? mining;

		private static FieldInfo? factor;

		internal static bool Ready { get; private set; } = true;


		internal static void Initialize(Harmony harmony)
		{
			//IL_0119: Unknown result type (might be due to invalid IL or missing references)
			//IL_0127: Expected O, but got Unknown
			if (Chainloader.PluginInfos.TryGetValue("org.bepinex.plugins.mining", out var value))
			{
				flag = ((object)value.Instance).GetType().GetNestedType("SetMiningFlag", BindingFlags.Public);
				handle = ((flag == null) ? null : AccessTools.Method(flag, "HandleMining", (Type[])null, (Type[])null));
				mining = ((flag == null) ? null : AccessTools.Field(flag, "IsMining"));
				factor = ((flag == null) ? null : AccessTools.Field(flag, "MiningFactor"));
				Ready = handle != null && mining != null && factor != null && value.Metadata.Version.ToString() == "1.1.6";
				if (!Ready)
				{
					Plugin.Instance.Log.LogError((object)"Unknown Smoothbrain Mining API/version. Shared mining is disabled to protect compatibility.");
				}
				else
				{
					harmony.Patch((MethodBase)handle, new HarmonyMethod(typeof(MiningCompatibility), "BeforeHandle", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				}
			}
		}

		private static bool BeforeHandle(ref bool __result)
		{
			if (!Plugin.Completing)
			{
				return true;
			}
			__result = true;
			return false;
		}

		internal static float Factor(Player player)
		{
			return ((Component)player).GetComponent<ZNetView>().GetZDO().GetFloat("Mining Skill Factor", 0f);
		}

		internal static void Contribute(HitData hit, Deposit deposit)
		{
			if (!(handle == null))
			{
				Character attacker = hit.GetAttacker();
				Player val = (Player)(object)((attacker is Player) ? attacker : null);
				if ((val != null))
				{
					((Component)val).GetComponent<ZNetView>().InvokeRPC("Mining IncreaseSkill", new object[1] { 1 });
				}
			}
		}

		internal static IDisposable Scope(float value)
		{
			return new MiningScope(value);
		}
	}
	internal static class MiningHud
	{
		private static GameObject? root;

		private static Text? heading;

		private static Text? percent;

		private static Text? activity;

		private static RectTransform? fill;

		private static Texture2D? leatherTexture;

		private static Sprite? leatherSprite;

		private static CanvasScaler? scaler;

		private static RectTransform? highlight;

		private static int lastPercent = -1;

		private static float lastBarProgress = -1f;

		private static float lastSpeed = float.NaN;

		private static int lastMiners = int.MinValue;

		private static int lastScreenWidth;

		private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
		{
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_0039: Unknown result type (might be due to invalid IL or missing references)
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0050: Unknown result type (might be due to invalid IL or missing references)
			//IL_005e: Unknown result type (might be due to invalid IL or missing references)
			//IL_006d: Unknown result type (might be due to invalid IL or missing references)
			RectTransform component = new GameObject(name, new Type[1] { typeof(RectTransform) }).GetComponent<RectTransform>();
			((Transform)component).SetParent(parent, false);
			Vector2 val = default(Vector2);
			val = new Vector2(0f, 1f);
			component.anchorMax = val;
			component.anchorMin = val;
			component.pivot = new Vector2(0f, 1f);
			component.anchoredPosition = new Vector2(x, 0f - y);
			component.sizeDelta = new Vector2(w, h);
			return component;
		}

		private static Image Image(RectTransform rect, Color color, Sprite? sprite = null)
		{
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			Image val = ((Component)rect).gameObject.AddComponent<Image>();
			((Graphic)val).raycastTarget = false;
			((Graphic)val).color = color;
			val.sprite = sprite;
			if ((sprite != null))
			{
				val.type = UnityEngine.UI.Image.Type.Sliced;
			}
			return val;
		}

		private static Text Text(RectTransform rect, int size, TextAnchor alignment, bool bold = false)
		{
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
			Text obj = ((Component)rect).gameObject.AddComponent<Text>();
			obj.font = (bold ? GUIManager.Instance.AveriaSerifBold : GUIManager.Instance.AveriaSerif);
			obj.fontSize = size;
			obj.alignment = alignment;
			((Graphic)obj).color = new Color(0.97f, 0.9f, 0.72f);
			obj.supportRichText = false;
			((Graphic)obj).raycastTarget = false;
			obj.horizontalOverflow = (HorizontalWrapMode)1;
			return obj;
		}

		private static Sprite PanelSprite()
		{
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0056: Expected O, but got Unknown
			//IL_0146: Unknown result type (might be due to invalid IL or missing references)
			//IL_0155: Unknown result type (might be due to invalid IL or missing references)
			if ((leatherSprite != null))
			{
				return leatherSprite;
			}
			using Stream stream = typeof(MiningHud).Assembly.GetManifestResourceStream("MineTogether.hud.png") ?? throw new InvalidOperationException("HUD texture resource missing");
			using MemoryStream memoryStream = new MemoryStream();
			stream.CopyTo(memoryStream);
			leatherTexture = new Texture2D(2, 2, (TextureFormat)4, false);
			if (!AssetUtils.LoadImage(leatherTexture, memoryStream.ToArray()))
			{
				throw new InvalidOperationException("HUD texture failed to load");
			}
			((Texture)leatherTexture).wrapMode = (TextureWrapMode)1;
			((Texture)leatherTexture).filterMode = (FilterMode)1;
			Color32[] pixels = leatherTexture.GetPixels32();
			int width = ((Texture)leatherTexture).width;
			int height = ((Texture)leatherTexture).height;
			int num = width;
			int num2 = height;
			int num3 = -1;
			int num4 = -1;
			for (int i = 0; i < height; i++)
			{
				for (int j = 0; j < width; j++)
				{
					if (pixels[i * width + j].a >= 64)
					{
						num = Math.Min(num, j);
						num2 = Math.Min(num2, i);
						num3 = Math.Max(num3, j);
						num4 = Math.Max(num4, i);
					}
				}
			}
			if (num3 < 0)
			{
				throw new InvalidOperationException("HUD texture is empty");
			}
			leatherSprite = Sprite.Create(leatherTexture, new Rect((float)num, (float)num2, (float)(num3 - num + 1), (float)(num4 - num2 + 1)), new Vector2(0.5f, 0.5f), 100f, 0u, (SpriteMeshType)0);
			return leatherSprite;
		}

		private static void Create()
		{

			// A rebuilt HUD has fresh, empty labels, so the change gates must start from "unknown".
			lastPercent = -1;
			lastBarProgress = -1f;
			lastScreenWidth = 0;
			lastSpeed = float.NaN;
			lastMiners = int.MinValue;
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			//IL_003c: Expected O, but got Unknown
			//IL_0090: Unknown result type (might be due to invalid IL or missing references)
			//IL_0096: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
			//IL_015a: Unknown result type (might be due to invalid IL or missing references)
			//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
			//IL_0220: Unknown result type (might be due to invalid IL or missing references)
			//IL_026c: Unknown result type (might be due to invalid IL or missing references)
			root = new GameObject("VeinMiningHud", new Type[3]
			{
				typeof(RectTransform),
				typeof(Canvas),
				typeof(CanvasScaler)
			});
			Canvas component = root.GetComponent<Canvas>();
			component.renderMode = (RenderMode)0;
			component.sortingOrder = 20;
			RectTransform val = Rect("MiningPanel", root.transform, 0f, 0f, 360f, 84f);
			Vector2 val2 = default(Vector2);
			val2 = new Vector2(0.5f, 0.29f);
			val.anchorMax = val2;
			val.anchorMin = val2;
			val.pivot = new Vector2(0.5f, 0.5f);
			val.anchoredPosition = Vector2.zero;
			Image(val, Color.white, PanelSprite()).type = UnityEngine.UI.Image.Type.Simple;
			heading = Text(Rect("Title", (Transform)(object)val, 18f, 11f, 230f, 22f), 16, (TextAnchor)3, bold: true);
			percent = Text(Rect("Percent", (Transform)(object)val, 264f, 11f, 78f, 22f), 16, (TextAnchor)5, bold: true);
			Image(Rect("BarBorder", (Transform)(object)val, 18f, 37f, 324f, 13f), new Color(0.65f, 0.43f, 0.17f), GUIManager.Instance.GetSprite("UISprite"));
			Image(Rect("BarTrack", (Transform)(object)val, 19f, 38f, 322f, 11f), new Color(0.05f, 0.05f, 0.035f), GUIManager.Instance.GetSprite("UISprite"));
			RectTransform val3 = Rect("ProgressClip", (Transform)(object)val, 20f, 39f, 320f, 9f);
			((Component)val3).gameObject.AddComponent<RectMask2D>();
			fill = Rect("Progress", (Transform)(object)val3, 0f, 0f, 320f, 9f);
			Image(fill, new Color(0.93f, 0.58f, 0.12f), GUIManager.Instance.GetSprite("UISprite"));
			Image(Rect("Highlight", (Transform)(object)fill, 0f, 0f, 320f, 2f), new Color(0.98f, 0.76f, 0.36f));
			activity = Text(Rect("Activity", (Transform)(object)val, 18f, 54f, 324f, 20f), 13, (TextAnchor)4);
			heading.text = "Mine Together";
			scaler = root.GetComponent<CanvasScaler>();
			highlight = ((Component)((Transform)fill).GetChild(0)).GetComponent<RectTransform>();
		}

		internal static void Draw(float progress, int miners, float oreSpeed, bool automatic)
		{
			if ((Hud.instance == null) || (GUIManager.Instance.AveriaSerif == null))
			{
				return;
			}
			if (root == null)
			{
				Create();
			}
			root.SetActive(true);

			// Everything below used to run every frame: a GetComponent, a GetChild().GetComponent()
			// and two interpolated strings. The bar only moves when the pixel width changes, and the
			// labels only change when the number they show changes.
			if (Screen.width != lastScreenWidth)
			{
				lastScreenWidth = Screen.width;
				scaler.scaleFactor = Mathf.Clamp((float)Screen.width / 1280f, 0.85f, 1f);
			}

			float clamped = Mathf.Clamp01(progress);
			if (Mathf.Abs(clamped - lastBarProgress) > 0.002f)
			{
				lastBarProgress = clamped;
				fill.sizeDelta = new Vector2(320f * clamped, 9f);
				highlight.sizeDelta = new Vector2(fill.sizeDelta.x, 2f);
			}

			int rounded = Mathf.RoundToInt(clamped * 100f);
			if (rounded != lastPercent)
			{
				lastPercent = rounded;
				percent.text = rounded.ToString() + "%";
			}

			// Compare the inputs, not the built string: formatting every frame to discover that
			// nothing changed would still allocate two strings per frame.
			if (oreSpeed != lastSpeed || miners != lastMiners)
			{
				lastSpeed = oreSpeed;
				lastMiners = miners;
				string arg = ((miners == 1) ? "1 miner" : Mathf.Max(0, miners).ToString() + " miners");
				activity.text = oreSpeed.ToString("0.##") + "\u00d7 \u00b7 " + arg;
			}
		}

		internal static void Hide()
		{
			if ((root != null))
			{
				root.SetActive(false);
			}
		}

		internal static void Destroy()
		{
			if ((root != null))
			{
				Object.Destroy((Object)root);
			}
			root = null;
		}
	}
	
	[BepInPlugin("com.bootstrapboys.minetogether", "Mine Together", "0.2.1")]
	[BepInDependency("_shudnal.ConditionalConfigSync", BepInDependency.DependencyFlags.HardDependency)]
	[BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
	[BepInDependency("org.bepinex.plugins.mining", BepInDependency.DependencyFlags.SoftDependency)]
	[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
	public sealed class Plugin : BaseUnityPlugin
	{


		public const string Id = "com.bootstrapboys.minetogether";

		public const string Version = "0.2.1";

		internal static Plugin Instance;

		internal static bool Completing;

		internal ConfigEntry<bool> Enabled;
		internal ConfigEntry<bool> VeinMine;
		internal ConfigEntry<bool> ResourceMultiplier;
		internal ConfigEntry<bool> FallingDeposits;
		internal ConfigEntry<bool> AreaMining;

		internal ConfigEntry<KeyboardShortcut> ToggleKey;

		internal ConfigEntry<string> Included;

		internal ConfigEntry<string> Excluded;

		internal ConfigEntry<float> Range;

		internal ConfigEntry<float> SpeedPerPlayer;

		internal ConfigEntry<float> SpeedRange;

		private Harmony harmony;

		private ZRoutedRpc? bound;

		private ZNetScene? audited;

		private float auditAt;

		internal ManualLogSource Log => Logger;

		private void Awake()
		{
			//IL_0021: Unknown result type (might be due to invalid IL or missing references)
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Expected O, but got Unknown
			//IL_0073: Unknown result type (might be due to invalid IL or missing references)
			//IL_007d: Expected O, but got Unknown
			//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b6: Expected O, but got Unknown
			//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fd: Expected O, but got Unknown
			//IL_0131: Unknown result type (might be due to invalid IL or missing references)
			//IL_013b: Expected O, but got Unknown
			//IL_0146: Unknown result type (might be due to invalid IL or missing references)
			//IL_0150: Expected O, but got Unknown
			Instance = this;
			// Opened before anything else, so even a binding failure leaves a file naming the DLL
			// that actually loaded. The switch state is added once the settings exist.
			Diag.Open();
			// Layout: one section per feature, the module's own switch first. Every entry is
			// registered with Conditional Config Sync - Host() entries are the server's and are
			// locked for clients, Local() entries stay the player's own.
			Enabled = ServerConfig.Host(Config.Bind("1. General", "Enabled", true,
				"Enables/Disables the mod."));
			ToggleKey = ServerConfig.Local(Config.Bind("1. General", "ToggleKey",
				new KeyboardShortcut(KeyCode.B, KeyCode.LeftAlt),
				"Hold Alt and press B to toggle vein mining on and off."));

			VeinMine = ServerConfig.Local(Config.Bind("2. Vein Mining", "VeinMine", true,
				"Vein mining is always active while this is on. ToggleKey flips it at runtime."));
			SpeedPerPlayer = ServerConfig.Host(Config.Bind("2. Vein Mining", "MiningSpeedMultiplierPerPlayer", 0.5f,
				new ConfigDescription("Speed added per extra player. 0.5 means one player mines at 100% of the base rate, two at 150%, three at 200%. 0 turns the bonus off.",
					new AcceptableValueRange<float>(0f, 5f))));
			SpeedRange = ServerConfig.Host(Config.Bind("2. Vein Mining", "MultiplierPlayerRange", 10f,
				new ConfigDescription("Metres around the deposit in which a player counts towards MiningSpeedMultiplierPerPlayer.",
					new AcceptableValueRange<float>(1f, 64f))));
			Range = ServerConfig.Host(Config.Bind("2. Vein Mining", "MaximumDistance", 6f,
				new ConfigDescription("Maximum contributor distance to an exposed deposit collider.",
					new AcceptableValueRange<float>(2f, 12f))));
			Included = ServerConfig.Host(Config.Bind("2. Vein Mining", "AdditionalPrefabs", "",
				"Extra prefab names treated as ore deposits."));
			Excluded = ServerConfig.Host(Config.Bind("2. Vein Mining", "ExcludedPrefabs", "MineRock_Tin",
				"Exact prefab names, comma separated, that are never treated as ore deposits. " +
				"Tin is excluded by default so vein mining leaves the small surface rocks alone."));

			ResourceMultiplier = ServerConfig.Host(Config.Bind("3. Resources", "ResourceMultiplier", true,
				"Scale world resources (mining, lumber, pickables, crates, dungeon loot)."));
			FallingDeposits = ServerConfig.Host(Config.Bind("4. Rock Collapse", "FallingDeposits", true,
				"Rock pieces fall once the deposit no longer rests on enough ground. Vanilla leaves them floating."));
			AreaMining = ServerConfig.Host(Config.Bind("5. AOE Mining", "AreaMining", true,
				"Spread a pickaxe swing over the rocks around the impact point. Never runs while vein mining is active."));

			ResourceYield.Bind(Config);
			RockCollapse.Bind(Config);
			AoeMining.Bind(Config);
			Diag.Bind(Config);
			Diag.State();
			harmony = new Harmony("com.bootstrapboys.minetogether");
			Diag.Trace("[Awake]", "harmony created");
			// PatchAll is all-or-nothing: one unresolvable patch class throws and every class after
			// it is silently left unpatched. Patch each class on its own instead, so a single miss
			// costs one feature and names itself in the log.
			PatchEach(harmony);
			Diag.Trace("[Awake]", "patched");
			try
			{
				MiningCompatibility.Initialize(harmony);
			}
			catch (Exception compatError)
			{
				Diag.Error("[Awake]", compatError);
				throw;
			}
			Diag.Trace("[Awake]", "compat initialised");
			Diag.StartupDumps(harmony, Config);
			Diag.Trace("[Awake]", "startup dumps done");
			Logger.LogInfo((object)$"Mine Together {Version} loaded: {Switches.Describe()} " +
				$"| yield x{ResourceYield.MiningMultiplier.Value}/{ResourceYield.LumberMultiplier.Value}/" +
				$"{ResourceYield.PickableMultiplier.Value}/{ResourceYield.StructureMultiplier.Value} " +
				$"| diagnostics {(Diag.Enabled ? Diag.Path : "off")}");
		}

		private static void PatchEach(Harmony harmony)
		{
			int applied = 0;
			foreach (Type type in typeof(Plugin).Assembly.GetTypes())
			{
				if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
				try
				{
					harmony.CreateClassProcessor(type).Patch();
					applied++;
					Diag.Trace("[Patch]", "applied " + type.Name);
				}
				catch (Exception e)
				{
					Diag.Error("[Patch]", type.Name + ": " + e.Message);
				}
			}
			Diag.Trace("[Patch]", $"{applied} patch classes applied");
		}

		private void Update()
		{
			Diag.TraceOnce("tick:plugin", "[Tick]", "plugin update running");
			// Master switch on the host: end sessions still running when it is turned off, so a
			// client whose own switch is on cannot keep mining through the server's setting.
			if (Server.IsServer && !Switches.VeinMine)
			{
				Server.Shutdown("Mine Together is disabled on this server.");
			}
			if (ZRoutedRpc.instance != bound)
			{
				bound = ZRoutedRpc.instance;
				Server.Reset();
				Client.Reset();
				if (bound != null)
				{
					Server.Register(bound);
				}
			}
			if ((Object)ZNetScene.instance != (Object)audited)
			{
				audited = ZNetScene.instance;
				auditAt = Time.time + 8f;
			}
			if ((audited != null) && auditAt > 0f && Time.time >= auditAt)
			{
				auditAt = 0f;
				try
				{
					Catalog.Audit();
				}
				catch (Exception ex)
				{
					Diag.Error("[Audit]", ex);
				}
				Diag.Summary();
			}
			Server.UpdateActivity();
			Client.Update();
			Client.Draw();
		}

		private void OnDestroy()
		{
			Diag.Close();
			MiningHud.Destroy();
			Harmony obj = harmony;
			if (obj != null)
			{
				obj.UnpatchSelf();
			}
			Client.Reset();
			Server.Reset();
		}
	}
	internal static class Server
	{
		private sealed class Session
		{
			internal ZDOID Target;

			internal ZDOID Player;

			internal long Token;

			internal int Sequence;

			internal float LastHit = -10f;

			internal float LastSeen;
		}

		[CompilerGenerated]
		private sealed class ManualAfterLoadEnum : IEnumerator<object>, IDisposable, IEnumerator
		{
			private int _state;

			private object _current;

			public long sender;

			public HitData hit;

			public ZDOID id;

			public int index;

			object IEnumerator<object>.Current
			{
				[DebuggerHidden]
				get
				{
					return _current;
				}
			}

			object IEnumerator.Current
			{
				[DebuggerHidden]
				get
				{
					return _current;
				}
			}

			[DebuggerHidden]
			public ManualAfterLoadEnum(int _state)
			{
				this._state = _state;
			}

			[DebuggerHidden]
			void IDisposable.Dispose()
			{
				_state = -2;
			}

			private bool MoveNext()
			{
				//IL_003a: Unknown result type (might be due to invalid IL or missing references)
				//IL_0046: Unknown result type (might be due to invalid IL or missing references)
				//IL_007c: Unknown result type (might be due to invalid IL or missing references)
				switch (_state)
				{
				default:
					return false;
				case 0:
					_state = -1;
					_current = null;
					_state = 1;
					return true;
				case 1:
					_state = -1;
					try
					{
						Player val = PlayerFor(sender, hit.m_attacker);
						Deposit deposit = Deposit.Find(id);
						if (!(val != null) || deposit == null || index < 0 || index >= deposit.Parts.Length || deposit.Distance(((Component)val).transform.position) > Plugin.Instance.Range.Value)
						{
							return false;
						}
						deposit.Data.SetOwner(ServerId);
						if (deposit.Rock is MineRock5)
						{
							GameRefs.MineRock5RpcDamage.Invoke(deposit.Rock, new object[3] { sender, hit, index });
						}
						else if (deposit.Rock is MineRock)
						{
							GameRefs.MineRockRpcHit.Invoke(deposit.Rock, new object[3] { sender, hit, index });
						}
						else
						{
							GameRefs.DestructibleRpcDamage.Invoke(deposit.Rock, new object[2] { sender, hit });
						}
					}
					catch (Exception ex)
					{
						Plugin.Instance.Log.LogError((object)("Manual mining failed: " + ex));
					}
					return false;
				}
			}

			bool IEnumerator.MoveNext()
			{
				//ILSpy generated this explicit interface implementation from .override directive in MoveNext
				return this.MoveNext();
			}

			[DebuggerHidden]
			void IEnumerator.Reset()
			{
				throw new NotSupportedException();
			}
		}

		[CompilerGenerated]
		private sealed class StartAfterLoadEnum : IEnumerator<object>, IDisposable, IEnumerator
		{
			private int _state;

			private object _current;

			public long sender;

			public ZDOID playerId;

			public ZDOID id;

			object IEnumerator<object>.Current
			{
				[DebuggerHidden]
				get
				{
					return _current;
				}
			}

			object IEnumerator.Current
			{
				[DebuggerHidden]
				get
				{
					return _current;
				}
			}

			[DebuggerHidden]
			public StartAfterLoadEnum(int _state)
			{
				this._state = _state;
			}

			[DebuggerHidden]
			void IDisposable.Dispose()
			{
				_state = -2;
			}

			private bool MoveNext()
			{
				//IL_017a: Unknown result type (might be due to invalid IL or missing references)
				//IL_0035: Unknown result type (might be due to invalid IL or missing references)
				//IL_0041: Unknown result type (might be due to invalid IL or missing references)
				//IL_0080: Unknown result type (might be due to invalid IL or missing references)
				//IL_005e: Unknown result type (might be due to invalid IL or missing references)
				//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
				//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
				//IL_0106: Unknown result type (might be due to invalid IL or missing references)
				//IL_010b: Unknown result type (might be due to invalid IL or missing references)
				//IL_013b: Unknown result type (might be due to invalid IL or missing references)
				switch (_state)
				{
				default:
					return false;
				case 0:
					_state = -1;
					_current = null;
					_state = 1;
					return true;
				case 1:
					_state = -1;
					try
					{
						Player val = PlayerFor(sender, playerId);
						Deposit deposit = Deposit.Find(id);
						if (!(val != null) || deposit == null || deposit.Distance(((Component)val).transform.position) > Plugin.Instance.Range.Value)
						{
							Diag.Trace("[Vein]", $"start aborted: player={(val != null)} deposit={(deposit != null)} distance={(deposit != null && val != null ? deposit.Distance(((Component)val).transform.position).ToString("0.#") : "-")} range={Plugin.Instance.Range.Value}");
							Reply(sender, id, 0L, "This deposit is unavailable or is not supported.");
							return false;
						}
						WorkLedger ledger = deposit.Load();
						deposit.Data.SetOwner(ServerId);
						deposit.Data.Set(Protocol.Active, true);
						deposit.Data.Set(Protocol.SpeedKey, SpeedRatio(id));
						deposit.Save(ledger);
						Sessions[sender] = new Session
						{
							Target = id,
							Player = playerId,
							Token = ++nextToken,
							LastSeen = Time.time
						};
						Reply(sender, id, nextToken, "Vein mining started.");
					}
					catch (Exception ex)
					{
						Plugin.Instance.Log.LogWarning((object)("Cannot start shared mining: " + ex.Message));
						Reply(sender, id, 0L, "Could not start mining. Check the server log.");
					}
					return false;
				}
			}

			bool IEnumerator.MoveNext()
			{
				//ILSpy generated this explicit interface implementation from .override directive in MoveNext
				return this.MoveNext();
			}

			[DebuggerHidden]
			void IEnumerator.Reset()
			{
				throw new NotSupportedException();
			}
		}
		/// <summary>What "100% mining speed" means: five times the vanilla work per swing.</summary>
		private const float BaseWork = 5f;

		private static readonly Dictionary<long, Session> Sessions = new Dictionary<long, Session>();

		private static long nextToken;
		private static readonly HashSet<ZDOID> ActivityTargets = new HashSet<ZDOID>();

		private static float nextActivity;

		private static readonly Dictionary<ZDOID, float> Pins = new Dictionary<ZDOID, float>();

		internal static bool IsServer
		{
			get
			{
				if ((ZNet.instance != null))
				{
					return ZNet.instance.IsServer();
				}
				return false;
			}
		}

		internal static long ServerId
		{
			get
			{
				if (ZRoutedRpc.instance != null)
				{
					return (long)GameRefs.GetServerPeerId.Invoke(ZRoutedRpc.instance, null);
				}
				return 0L;
			}
		}

		internal static void Reset()
		{
			Sessions.Clear();
			Pins.Clear();
			ActivityTargets.Clear();
			nextActivity = 0f;
			ManualMining.Reset();
		}

		private static Session[] ActiveMiners(ZDOID id)
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			return Sessions.Values.Where((Session s) => s.Target == id && Time.time - s.LastHit <= 4f && Time.time - s.LastSeen <= 15f).ToArray();
		}

		/// <summary>
		/// Mining speed for one deposit: 1 plus the per-player bonus for every miner working it
		/// from within MultiplierPlayerRange. Only the host computes this; clients read the value
		/// written to the deposit.
		/// </summary>
		/// <summary>
		/// The work factor for a given miner count. Pure, so the harness can assert the curve
		/// without a session: 1 -> 5 (100%), 2 -> 7.5 (150%), 3 -> 10 (200%).
		/// </summary>
		internal static float SpeedFor(int miners)
		{
			float perPlayer = Plugin.Instance != null ? Plugin.Instance.SpeedPerPlayer.Value : 0.5f;
			return BaseWork * (1f + Mathf.Max(0, miners - 1) * perPlayer);
		}

		/// <summary>The same curve as the HUD shows it: 1x, 1.5x, 2x.</summary>
		internal static float RatioFor(int miners)
		{
			return SpeedFor(miners) / BaseWork;
		}

		/// <summary>The HUD figure: 1x for a single miner, 1.5x for two, relative to the baseline.</summary>
		internal static float SpeedRatio(ZDOID target)
		{
			return SpeedFactor(target) / BaseWork;
		}

		internal static float SpeedFactor(ZDOID target)
		{
			Plugin plugin = Plugin.Instance;
			if (plugin == null) return 1f;

			float perPlayer = plugin.SpeedPerPlayer.Value;

			Session[] miners = ActiveMiners(target);
			if (miners.Length == 0) return BaseWork;

			ZDO centre = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(target) : null;
			if (centre == null) return SpeedFor(miners.Length);

			Vector3 origin = centre.GetPosition();
			float range = plugin.SpeedRange.Value;
			int counted = 0;
			for (int i = 0; i < miners.Length; i++)
			{
				ZDO player = ZDOMan.instance.GetZDO(miners[i].Player);
				if (player == null || Vector3.Distance(player.GetPosition(), origin) <= range) counted++;
			}
			// 100% is BaseWork times vanilla; each extra miner adds perPlayer of that baseline.
			return SpeedFor(counted);
		}

		internal static void UpdateActivity()
		{
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_0085: Unknown result type (might be due to invalid IL or missing references)
			//IL_008a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0096: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
			//IL_0179: Unknown result type (might be due to invalid IL or missing references)
			if (!IsServer || Time.time < nextActivity)
			{
				return;
			}
			nextActivity = Time.time + 0.5f;
			foreach (Session value in Sessions.Values)
			{
				ActivityTargets.Add(value.Target);
			}
			ZDOID[] array = ActivityTargets.ToArray();
			foreach (ZDOID id in array)
			{
				ZDO zDO = ZDOMan.instance.GetZDO(id);
				if (zDO == null)
				{
					ActivityTargets.Remove(id);
					continue;
				}
				Session[] array2 = ActiveMiners(id);
				zDO.Set(Protocol.MinersKey, array2.Length);
				zDO.Set(Protocol.SpeedKey, SpeedRatio(id));
				zDO.Set(Protocol.ActivityTimeKey, (long)ZNet.instance.GetTimeSeconds());
				zDO.Set(Protocol.NamesKey, string.Join(", ", array2.Select(delegate(Session s)
				{
					//IL_0006: Unknown result type (might be due to invalid IL or missing references)
					GameObject val = ZNetScene.instance.FindInstance(s.Player);
					Player val2 = ((val != null) ? val.GetComponent<Player>() : null);
					string text = ((val2 != null) ? val2.GetPlayerName() : "Miner");
					return (text.Length <= 24) ? text : text.Substring(0, 24);
				}).Take(6)));
				if (array2.Length == 0 && !Sessions.Values.Any((Session s) => s.Target == id))
				{
					ActivityTargets.Remove(id);
				}
			}
		}

		internal static void Register(ZRoutedRpc rpc)
		{
			rpc.Register<ZPackage>(Protocol.Start, (Action<long, ZPackage>)Start);
			rpc.Register<ZPackage>(Protocol.Stop, (Action<long, ZPackage>)Stop);
			rpc.Register<ZPackage>(Protocol.Hit, (Action<long, ZPackage>)Hit);
			rpc.Register<ZPackage>(Protocol.Reply, (Action<long, ZPackage>)Client.Reply);
			rpc.Register<ZPackage>(Protocol.Keep, (Action<long, ZPackage>)Keep);
			rpc.Register<ZPackage>(Protocol.Manual, (Action<long, ZPackage>)Manual);
			rpc.Register<ZPackage>(Protocol.Move, (Action<long, ZPackage>)Client.Retarget);
		}

		private static void Manual(long sender, ZPackage pkg)
		{
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Expected O, but got Unknown
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_004f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0080: Unknown result type (might be due to invalid IL or missing references)
			//IL_0087: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
			//IL_0104: Unknown result type (might be due to invalid IL or missing references)
			if (!IsServer)
			{
				return;
			}
			try
			{
				if (pkg.GetArray().Length <= 2048)
				{
					ZDOID val = pkg.ReadZDOID();
					int index = pkg.ReadInt();
					HitData val2 = new HitData();
					val2.Deserialize(ref pkg);
					ZDO zDO = ZDOMan.instance.GetZDO(val2.m_attacker);
					ZDO zDO2 = ZDOMan.instance.GetZDO(val);
					if (zDO != null && zDO2 != null && zDO.GetOwner() == sender && zDO2.GetBool(Protocol.Active, false) && !(Vector3.Distance(zDO.GetPosition(), zDO2.GetPosition()) > 80f) && WorkLedger.Finite(val2.GetTotalDamage()) && !(val2.GetTotalDamage() <= 0f) && !(val2.GetTotalDamage() > 100000f) && WorkLedger.Finite(val2.m_damage.m_pickaxe) && !(val2.m_damage.m_pickaxe <= 0f) && EnsureLoaded(val2.m_attacker, player: true, sender) && EnsureLoaded(val, player: false, sender))
					{
						((MonoBehaviour)Plugin.Instance).StartCoroutine(ManualAfterLoad(sender, val, index, val2));
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Instance.Log.LogWarning((object)("Manual mining request failed: " + ex.Message));
			}
		}

		[IteratorStateMachine(typeof(ManualAfterLoadEnum))]
		private static IEnumerator ManualAfterLoad(long sender, ZDOID id, int index, HitData hit)
		{
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
			return new ManualAfterLoadEnum(0)
			{
				sender = sender,
				id = id,
				index = index,
				hit = hit
			};
		}

		internal static void Retarget(ZDOID previous, ZDOID next)
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_004a: Unknown result type (might be due to invalid IL or missing references)
			//IL_004f: Unknown result type (might be due to invalid IL or missing references)
			//IL_005e: Unknown result type (might be due to invalid IL or missing references)
			//IL_005f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0064: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Expected O, but got Unknown
			//IL_006b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0072: Unknown result type (might be due to invalid IL or missing references)
			ActivityTargets.Add(previous);
			ActivityTargets.Add(next);
			Pins[next] = Time.time + 15f;
			foreach (KeyValuePair<long, Session> session in Sessions)
			{
				if (!(session.Value.Target != previous))
				{
					session.Value.Target = next;
					ZPackage val = new ZPackage();
					val.Write(previous);
					val.Write(next);
					val.Write(session.Value.Token);
					ZRoutedRpc.instance.InvokeRoutedRPC(session.Key, Protocol.Move, new object[1] { val });
				}
			}
		}

		internal static void Shutdown(string message)
		{
			if (Sessions.Count == 0)
			{
				return;
			}
			Diag.Trace("[Vein]", $"host ended {Sessions.Count} session(s): {message}");
			foreach (KeyValuePair<long, Session> session in Sessions)
			{
				Reply(session.Key, session.Value.Target, 0L, message);
			}
			Sessions.Clear();
		}

		internal static void Completed(ZDOID id)
		{
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			//IL_0076: Unknown result type (might be due to invalid IL or missing references)
			List<long> list = new List<long>();
			foreach (KeyValuePair<long, Session> session in Sessions)
			{
				if (session.Value.Target == id)
				{
					list.Add(session.Key);
				}
			}
			Diag.SessionsCompleted++;
			Diag.Trace("[Vein]", $"deposit {id} fully mined for {list.Count} miner(s)");
			foreach (long item in list)
			{
				Sessions.Remove(item);
				Reply(item, id, 0L, "Deposit fully mined.");
			}
		}

		private static bool EnsureLoaded(ZDOID id, bool player, long sender)
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_005d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0073: Unknown result type (might be due to invalid IL or missing references)
			ZDO zDO = ZDOMan.instance.GetZDO(id);
			if (zDO == null)
			{
				return false;
			}
			GameObject prefab = ZNetScene.instance.GetPrefab(zDO.GetPrefab());
			if ((prefab != null))
			{
				bool num;
				if (!player)
				{
					num = !Catalog.Supported(prefab);
				}
				else
				{
					if (!(prefab.GetComponent<Player>() != null))
					{
						goto IL_0056;
					}
					num = zDO.GetOwner() != sender;
				}
				if (!num)
				{
					Pins[id] = Time.time + 15f;
					if (!(ZNetScene.instance.FindInstance(id) != null))
					{
						GameRefs.CreateObject.Invoke(ZNetScene.instance, new object[1] { zDO });
					}
					return true;
				}
			}
			goto IL_0056;
			IL_0056:
			return false;
		}

		internal static void KeepInstances(List<ZDO> currentNearObjects)
		{
			//IL_0057: Unknown result type (might be due to invalid IL or missing references)
			//IL_0078: Unknown result type (might be due to invalid IL or missing references)
			//IL_0103: Unknown result type (might be due to invalid IL or missing references)
			//IL_0124: Unknown result type (might be due to invalid IL or missing references)
			//IL_0167: Unknown result type (might be due to invalid IL or missing references)
			//IL_016c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0173: Unknown result type (might be due to invalid IL or missing references)
			if (!IsServer)
			{
				return;
			}
			List<long> list = new List<long>();
			foreach (KeyValuePair<long, Session> session in Sessions)
			{
				if (Time.time - session.Value.LastSeen > 15f)
				{
					list.Add(session.Key);
					continue;
				}
				Pins[session.Value.Target] = Time.time + 1f;
				Pins[session.Value.Player] = Time.time + 1f;
			}
			foreach (long item in list)
			{
				Sessions.Remove(item);
			}
			List<ZDOID> list2 = new List<ZDOID>();
			foreach (KeyValuePair<ZDOID, float> pin in Pins)
			{
				ZDO zDO = ZDOMan.instance.GetZDO(pin.Key);
				if (Time.time > pin.Value || zDO == null)
				{
					list2.Add(pin.Key);
				}
				else if (!currentNearObjects.Contains(zDO))
				{
					currentNearObjects.Add(zDO);
				}
			}
			foreach (ZDOID item2 in list2)
			{
				Pins.Remove(item2);
			}
		}

		private static void Keep(long sender, ZPackage pkg)
		{
			//IL_0039: Unknown result type (might be due to invalid IL or missing references)
			//IL_0045: Unknown result type (might be due to invalid IL or missing references)
			//IL_008b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0062: Unknown result type (might be due to invalid IL or missing references)
			if (!IsServer)
			{
				return;
			}
			try
			{
				if (pkg.GetArray().Length <= 16 && Sessions.TryGetValue(sender, out Session value) && pkg.ReadLong() == value.Token)
				{
					Player val = PlayerFor(sender, value.Player);
					Deposit deposit = Deposit.Find(value.Target);
					if (!(val != null) || deposit == null || deposit.Distance(((Component)val).transform.position) > Plugin.Instance.Range.Value)
					{
						Sessions.Remove(sender);
						Reply(sender, value.Target, 0L, "Mining stopped. Shared progress has been saved.");
					}
					else
					{
						value.LastSeen = Time.time;
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Instance.Log.LogDebug((object)ex.Message);
			}
		}

		private static Player? PlayerFor(long sender, ZDOID id)
		{
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			GameObject val = ((ZNetScene.instance != null) ? ZNetScene.instance.FindInstance(id) : null);
			Player val2 = ((val != null) ? val.GetComponent<Player>() : null);
			ZNetView val3 = ((val2 != null) ? ((Component)val2).GetComponent<ZNetView>() : null);
			if (!(val3 != null) || !val3.IsValid() || val3.GetZDO().GetOwner() != sender || ((Character)val2).IsDead() || ((Character)val2).IsTeleporting())
			{
				return null;
			}
			return val2;
		}

		private static void Reply(long sender, ZDOID id, long token, string message)
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Expected O, but got Unknown
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			ZPackage val = new ZPackage();
			val.Write(id);
			val.Write(token);
			val.Write(message);
			ZRoutedRpc.instance.InvokeRoutedRPC(sender, Protocol.Reply, new object[1] { val });
		}

		private static void Start(long sender, ZPackage pkg)
		{
			//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Unknown result type (might be due to invalid IL or missing references)
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_004a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0056: Unknown result type (might be due to invalid IL or missing references)
			//IL_0099: Unknown result type (might be due to invalid IL or missing references)
			//IL_006d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0073: Unknown result type (might be due to invalid IL or missing references)
			//IL_0084: Unknown result type (might be due to invalid IL or missing references)
			//IL_008e: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
			//IL_00af: Unknown result type (might be due to invalid IL or missing references)
			if (!IsServer)
			{
				return;
			}

			// The host's master switch wins: a client whose own settings are on cannot start a
			// session the server has switched off.
			if (!Switches.VeinMine)
			{
				Diag.Trace("[Vein]", $"start refused for peer {sender}: disabled on this server");
				Reply(sender, ZDOID.None, 0L, "Mine Together is disabled on this server.");
				return;
			}
			ZDOID val = ZDOID.None;
			try
			{
				int bytes = pkg.GetArray().Length;
				int version = pkg.ReadInt();
				// A refusal here used to be silent, which made a bad packet look like a dead mod.
				Diag.Trace("[Vein]", $"start packet: bytes={bytes} version={version} ready={MiningCompatibility.Ready}");
				if (bytes <= 256 && version == 1 && MiningCompatibility.Ready)
				{
					val = pkg.ReadZDOID();
					ZDOID val2 = pkg.ReadZDOID();
					ZDO zDO = ZDOMan.instance.GetZDO(val2);
					ZDO zDO2 = ZDOMan.instance.GetZDO(val);
					if (zDO == null || zDO2 == null || zDO.GetOwner() != sender || Vector3.Distance(zDO.GetPosition(), zDO2.GetPosition()) > 80f || !EnsureLoaded(val2, player: true, sender) || !EnsureLoaded(val, player: false, sender))
					{
						Diag.Trace("[Vein]", $"start refused for peer {sender}: unsupported or too far");
						Reply(sender, val, 0L, "This deposit is not supported or is too far away.");
					}
					else
					{
						Diag.Trace("[Vein]", $"start accepted for peer {sender}: {val}");
						((MonoBehaviour)Plugin.Instance).StartCoroutine(StartAfterLoad(sender, val, val2));
					}
				}
				else
				{
					Reply(sender, val, 0L, "Could not start shared mining. Update the mod on both sides.");
				}
			}
			catch (Exception ex)
			{
				Plugin.Instance.Log.LogWarning((object)("Cannot load shared mining: " + ex.Message));
				Reply(sender, val, 0L, "Could not load the deposit. Check the server log.");
			}
		}

		[IteratorStateMachine(typeof(StartAfterLoadEnum))]
		private static IEnumerator StartAfterLoad(long sender, ZDOID id, ZDOID playerId)
		{
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
			return new StartAfterLoadEnum(0)
			{
				sender = sender,
				id = id,
				playerId = playerId
			};
		}

		private static void Stop(long sender, ZPackage pkg)
		{
			if (!IsServer)
			{
				return;
			}
			try
			{
				if (pkg.GetArray().Length <= 32)
				{
					long num = pkg.ReadLong();
					if (Sessions.TryGetValue(sender, out Session value) && value.Token == num)
					{
						Sessions.Remove(sender);
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Instance.Log.LogDebug((object)ex.Message);
			}
		}

		private static void Hit(long sender, ZPackage pkg)
		{
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			//IL_0085: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
			//IL_0102: Expected O, but got Unknown
			//IL_01df: Unknown result type (might be due to invalid IL or missing references)
			//IL_01eb: Unknown result type (might be due to invalid IL or missing references)
			//IL_0122: Unknown result type (might be due to invalid IL or missing references)
			//IL_0128: Unknown result type (might be due to invalid IL or missing references)
			//IL_0264: Unknown result type (might be due to invalid IL or missing references)
			//IL_0323: Unknown result type (might be due to invalid IL or missing references)
			if (!IsServer)
			{
				return;
			}
			try
			{
				if (pkg.GetArray().Length > 32768 || !Sessions.TryGetValue(sender, out Session value))
				{
					return;
				}
				long num = pkg.ReadLong();
				int num2 = pkg.ReadInt();
				int num3 = pkg.ReadInt();
				if (num != value.Token || num2 <= value.Sequence || num3 < 1 || num3 > 64 || Time.time - value.LastHit < 0.15f)
				{
					return;
				}
				Player val = PlayerFor(sender, value.Player);
				Deposit deposit = Deposit.Find(value.Target);
				if (!(val != null) || deposit == null || deposit.Distance(((Component)val).transform.position) > Plugin.Instance.Range.Value)
				{
					Sessions.Remove(sender);
					Reply(sender, value.Target, 0L, "Mining stopped. Shared progress has been saved.");
					return;
				}
				List<(int, HitData, float)> list = new List<(int, HitData, float)>();
				for (int i = 0; i < num3; i++)
				{
					int num4 = pkg.ReadInt();
					HitData val2 = new HitData();
					val2.Deserialize(ref pkg);
					if (num4 < 0 || num4 >= deposit.Health.Length || val2.m_attacker != value.Player || val2.m_toolTier < deposit.Tier || !WorkLedger.Finite(val2.m_damage.m_pickaxe) || val2.m_damage.m_pickaxe <= 0f || !WorkLedger.Finite(val2.GetTotalDamage()) || val2.GetTotalDamage() <= 0f || val2.GetTotalDamage() > 100000f)
					{
						return;
					}
					list.Add((num4, val2, 0f));
				}
				value.Sequence = num2;
				value.LastHit = (value.LastSeen = Time.time);
				ActivityTargets.Add(value.Target);
				float num5 = SpeedFactor(value.Target);
				deposit.Data.SetOwner(ServerId);
				WorkLedger workLedger = deposit.Load();
				float num6 = MiningCompatibility.Factor(val);
				HitData.DamageModifier val3 = default(HitData.DamageModifier);
				foreach (var item in list)
				{
					if (workLedger.Complete)
					{
						break;
					}
					int index = workLedger.Next(item.Item1);
					HitData obj = item.Item2.Clone();
					obj.ApplyResistance(deposit.ResistanceAt(index), out val3);
					float num7 = obj.GetTotalDamage() * num5;
					if (WorkLedger.Finite(num7) && !(num7 <= 0f))
					{
						MiningCompatibility.Contribute(item.Item2, deposit);
						int num8 = workLedger.Apply(num7, item.Item1);
						if (num8 >= 0)
						{
							deposit.Data.Set(Protocol.FactorPrefix + num8, num6);
						}
					}
				}
				deposit.Save(workLedger);
				if (workLedger.Complete)
				{
					deposit.Finish(list[list.Count - 1].Item2);
					Completed(value.Target);
					DropLift.Lift(deposit.Rock.transform.position);
				}
			}
			catch (Exception ex)
			{
				Plugin.Instance.Log.LogError((object)("Shared mining hit failed: " + ex));
			}
		}
	}
	[HarmonyPatch(typeof(ZNetScene), "RemoveObjects")]
	internal static class ServerInstances
	{
		private static void Prefix(List<ZDO> currentNearObjects)
		{
			Server.KeepInstances(currentNearObjects);
		}
	}
	[HarmonyPatch(typeof(ZDO), "SetOwner")]
	internal static class KeepServerOwnership
	{
		private static void Prefix(ZDO __instance, ref long uid)
		{
			if (__instance.GetBool(Protocol.Active, false) && Server.ServerId != 0L)
			{
				uid = Server.ServerId;
			}
		}
	}
	
	public sealed class WorkLedger
	{
		public float[] Remaining { get; }

		public float Total { get; }

		public bool Complete => Remaining.All((float x) => x <= 0f);

		public float Progress
		{
			get
			{
				if (!(Total > 0f))
				{
					return 1f;
				}
				return Math.Max(0f, Math.Min(1f, 1f - Remaining.Sum() / Total));
			}
		}

		public WorkLedger(float[] remaining, float total)
		{
			if (remaining.Length == 0 || remaining.Length > 4096 || remaining.Any((float x) => !Finite(x) || x < 0f) || !Finite(total) || total <= 0f || remaining.Sum() > total + 0.1f)
			{
				throw new ArgumentException("Invalid deposit state");
			}
			Remaining = (float[])remaining.Clone();
			Total = total;
		}

		/// <summary>
		/// Spends a swing on the vein, starting at the area the pickaxe struck.
		///
		/// Damage that is left after breaking an area carries into the next unbroken one, so a swing
		/// worth 225 against 50-health areas clears four of them instead of one. Without the carry the
		/// overkill was discarded, which made mining time equal to the area count and left the speed
		/// multiplier with nothing to do on ordinary deposits.
		/// </summary>
		public int Apply(float damage, int preferred)
		{
			if (!Finite(damage) || damage <= 0f || Complete)
			{
				return -1;
			}
			int first = Next(preferred);
			if (first < 0)
			{
				return -1;
			}

			float left = damage;
			int index = first;
			for (int visited = 0; visited < Remaining.Length && left > 0f; visited++)
			{
				float spend = Math.Min(left, Remaining[index]);
				Remaining[index] -= spend;
				left -= spend;
				if (left <= 0f)
				{
					break;
				}
				// Walk forward and wrap, so a swing near the end of the vein still spends its damage.
				int next = -1;
				for (int step = 1; step <= Remaining.Length; step++)
				{
					int candidate = (index + step) % Remaining.Length;
					if (Remaining[candidate] > 0f)
					{
						next = candidate;
						break;
					}
				}
				if (next < 0)
				{
					break;
				}
				index = next;
			}
			return first;
		}

		public int Next(int preferred)
		{
			if (preferred < 0 || preferred >= Remaining.Length || !(Remaining[preferred] > 0f))
			{
				return Array.FindIndex(Remaining, (float x) => x > 0f);
			}
			return preferred;
		}

		public void ReconcileNative(float[] before, float[] after)
		{
			// A mismatched array means the deposit changed shape mid-hit (broken area, stage swap).
			// Refusing to reconcile leaves the ledger alone, which is safe; throwing would abandon
			// the rest of the update half-applied.
			if (before.Length != Remaining.Length || after.Length != Remaining.Length ||
			    before.Any((float x) => !Finite(x) || x < 0f) || after.Any((float x) => !Finite(x) || x < 0f))
			{
				return;
			}
			for (int i = 0; i < Remaining.Length; i++)
			{
				Remaining[i] = ((after[i] <= 0f) ? 0f : Math.Max(0f, Remaining[i] - Math.Max(0f, before[i] - after[i])));
			}
		}

		public static bool Finite(float value)
		{
			if (!float.IsNaN(value))
			{
				return !float.IsInfinity(value);
			}
			return false;
		}
	}
}
