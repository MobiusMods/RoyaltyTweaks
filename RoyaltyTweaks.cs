using RimWorld;
using HarmonyLib;
using Verse;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;
using System.Runtime.Remoting.Messaging;

namespace TestMod
{

	// Token: 0x02000002 RID: 2
	[StaticConstructorOnStartup]
	public static class RoyaltyTweaks
	{
		// Token: 0x06000001 RID: 1 RVA: 0x00002050 File Offset: 0x00000250
		static RoyaltyTweaks()
		{
			new Harmony("com.mobius.royaltytweaks").PatchAll();
		}

		// Token: 0x02000005 RID: 5
		[HarmonyPatch(typeof(Pawn_RoyaltyTracker))]
		[HarmonyPatch("CanRequireThroneroom")]
		private static class Pawn_RoyaltyTracker_CanRequireThroneroom_Patch
		{
			// Token: 0x06000007 RID: 7 RVA: 0x000022C0 File Offset: 0x000004C0
			private static bool Prefix(Pawn_RoyaltyTracker __instance, Pawn ___pawn, ref bool __result)
			{
				if (!LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().throneRoomTweaks || ___pawn.ownership.AssignedThrone != null)
				{
					return true;
				}
				bool flag;
				if (___pawn.IsFreeColonist && __instance.allowRoomRequirements && !QuestUtility.IsQuestLodger(___pawn))
				{
					Map mapHeld = ___pawn.MapHeld;
					flag = (mapHeld != null && mapHeld.IsPlayerHome);
				}
				else
				{
					flag = false;
				}
				__result = flag;
				int highestSeniority = 0;
				bool throneAssigned = false;
				if (__result && __instance != null && __instance.MostSeniorTitle != null && __instance.MostSeniorTitle.def != null)
				{
					foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive_FreeColonists)
					{
						if (pawn != ___pawn && pawn.royalty != null && pawn.royalty.MostSeniorTitle != null && pawn.royalty.MostSeniorTitle.def != null && pawn.royalty.MostSeniorTitle.def.seniority > highestSeniority)
						{
							highestSeniority = pawn.royalty.MostSeniorTitle.def.seniority;
							if (pawn.ownership.AssignedThrone != null)
							{
								throneAssigned = true;
							}
						}
					}
					if (__instance.MostSeniorTitle.def.seniority < highestSeniority)
					{
						List<Pawn> spouses = null;
						if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().spouseWantsThroneroom)
						{
							spouses = SpouseRelationUtility.GetSpouses(___pawn, false);
						}
						if (spouses != null)
						{
							if (!spouses.All((Pawn a) => a.royalty == null))
							{
								if (!spouses.All((Pawn a) => a.royalty.MostSeniorTitle == null) && !spouses.All((Pawn a) => a.royalty.MostSeniorTitle.def.seniority < highestSeniority))
								{
									goto IL_1CD;
								}
							}
						}
						__result = false;
					}
					IL_1CD:
					if (__instance.MostSeniorTitle.def.seniority == highestSeniority && throneAssigned)
					{
						__result = false;
					}
				}
				return false;
			}
		}

		// Token: 0x02000006 RID: 6
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("CombinedDisabledWorkTags", MethodType.Getter)]
		private static class Pawn_CombinedDisabledWorkTags_Patch
		{
			// Token: 0x06000008 RID: 8 RVA: 0x000024CC File Offset: 0x000006CC
			private static string nameof(Action<float> applyOutcome)
			{
				throw new NotImplementedException();
			}

			// Token: 0x06000009 RID: 9 RVA: 0x000024D4 File Offset: 0x000006D4
			private static bool Prefix(Pawn __instance, ref WorkTags __result)
			{
				if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkPassionSkills)
				{
					WorkTags workTags = (__instance.story != null) ? __instance.story.DisabledWorkTagsBackstoryAndTraits : 0;
					WorkTags disabledRoyalTags = 0;
					Pawn_RoyaltyTracker royalty = __instance.royalty;
					bool flag;
					if (royalty == null)
					{
						flag = false;
					}
					else
					{
						RoyalTitle mostSeniorTitle = royalty.MostSeniorTitle;
						int? num;
						if (mostSeniorTitle == null)
						{
							num = null;
						}
						else
						{
							RoyalTitleDef def = mostSeniorTitle.def;
							num = ((def != null) ? new int?(def.seniority) : null);
						}
						int? num2 = num;
						int num3 = 100;
						flag = (num2.GetValueOrDefault() > num3 & num2 != null);
					}
					if (flag)
					{
						foreach (RoyalTitle royalTitle in __instance.royalty.AllTitlesForReading)
						{
							if (royalTitle.conceited)
							{
								disabledRoyalTags |= royalTitle.def.disabledWorkTags;
							}
						}
						List<string> namelist = new List<string>();
						if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkOnlyMajorPassionSkills)
						{
							namelist = (from a in __instance.skills.skills
										where a.passion == Passion.Major
										select a into b
										select b.def.defName).ToList<string>();
						}
						else
						{
							namelist = (from a in __instance.skills.skills
										where a.passion > Passion.None
										select a into b
										select b.def.defName).ToList<string>();
						}
						List<WorkTypeDef> list = DefDatabase<WorkTypeDef>.AllDefsListForReading;
						if (namelist != null && GenCollection.Any<string>(namelist))
						{
							IEnumerable<WorkTags> worklist = from a in list
															 where a.relevantSkills != null && GenCollection.Any<SkillDef>(a.relevantSkills) && namelist.Contains(a.relevantSkills.First<SkillDef>().defName)
															 select a into b
															 select b.workTags;
							if (worklist != null && worklist.Any<WorkTags>())
							{
								foreach (WorkTags worklistItem in worklist)
								{
									disabledRoyalTags &= ~worklistItem;
								}
							}
						}
					}
					workTags |= disabledRoyalTags;
					if (__instance.health != null && __instance.health.hediffSet != null)
					{
						foreach (Hediff hediff in __instance.health.hediffSet.hediffs)
						{
							HediffStage curStage = hediff.CurStage;
							if (curStage != null)
							{
								workTags |= curStage.disabledWorkTags;
							}
						}
					}
					__result = workTags;
					return false;
				}
				return true;
			}
		}

		// Token: 0x02000007 RID: 7
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("GetDisabledWorkTypes")]
		private static class GetDisabledWorkTypes_Patch
		{
			// Token: 0x0600000A RID: 10 RVA: 0x000027CC File Offset: 0x000009CC
			private static void Postfix(Pawn __instance, ref List<WorkTypeDef> __result, bool permanentOnly = false)
			{
				if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkPassionSkills && __instance.IsColonist)
				{
					Pawn_RoyaltyTracker royalty = __instance.royalty;
					bool flag;
					if (royalty == null)
					{
						flag = false;
					}
					else
					{
						RoyalTitle mostSeniorTitle = royalty.MostSeniorTitle;
						int? num;
						if (mostSeniorTitle == null)
						{
							num = null;
						}
						else
						{
							RoyalTitleDef def = mostSeniorTitle.def;
							num = ((def != null) ? new int?(def.seniority) : null);
						}
						int? num2 = num;
						int num3 = 100;
						flag = (num2.GetValueOrDefault() > num3 & num2 != null);
					}
					if (flag && __instance.royalty.MostSeniorTitle.conceited)
					{
						List<WorkTypeDef> removeList = new List<WorkTypeDef>();
						foreach (WorkTypeDef workType in __result)
						{
							List<SkillDef> skills = workType.relevantSkills;
							if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkOnlyMajorPassionSkills)
							{
								if (!GenCollection.Any<SkillRecord>(__instance.skills.skills, (SkillRecord a) => a.passion == Passion.Major && skills.Contains(a.def)))
								{
									removeList.Add(workType);
								}
							}
							else if (!GenCollection.Any<SkillRecord>(__instance.skills.skills, (SkillRecord a) => a.passion != Passion.None && skills.Contains(a.def)))
							{
								removeList.Add(workType);
							}
						}
						if (!GenList.NullOrEmpty<WorkTypeDef>(removeList))
						{
							__result = removeList;
						}
					}
				}
			}
		}

		// Token: 0x02000008 RID: 8
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("WorkTypeIsDisabled")]
		private static class WorkTypeIsDisabled_Patch
		{
			// Token: 0x0600000B RID: 11 RVA: 0x00002930 File Offset: 0x00000B30
			private static void Postfix(Pawn __instance, WorkTypeDef w, ref bool __result)
			{
				if (__result && LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkPassionSkills && __instance.IsColonist)
				{
					Pawn_RoyaltyTracker royalty = __instance.royalty;
					bool flag;
					if (royalty == null)
					{
						flag = false;
					}
					else
					{
						RoyalTitle mostSeniorTitle = royalty.MostSeniorTitle;
						int? num;
						if (mostSeniorTitle == null)
						{
							num = null;
						}
						else
						{
							RoyalTitleDef def = mostSeniorTitle.def;
							num = ((def != null) ? new int?(def.seniority) : null);
						}
						int? num2 = num;
						int num3 = 100;
						flag = (num2.GetValueOrDefault() > num3 & num2 != null);
					}
					if (flag && __instance.royalty.MostSeniorTitle.conceited)
					{
						List<SkillDef> skills = w.relevantSkills;
						if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkOnlyMajorPassionSkills)
						{
							if (GenCollection.Any<SkillRecord>(__instance.skills.skills, (SkillRecord a) => a.passion == Passion.Major && skills.Contains(a.def)))
							{
								__result = false;
								return;
							}
						}
						else if (GenCollection.Any<SkillRecord>(__instance.skills.skills, (SkillRecord a) => a.passion != Passion.None && skills.Contains(a.def)))
						{
							__result = false;
						}
					}
				}
			}
		}
	}

	// Token: 0x02000003 RID: 3
	public class RoyaltyTweaksSettings : ModSettings
	{
		// Token: 0x06000002 RID: 2 RVA: 0x00002064 File Offset: 0x00000264
		public override void ExposeData()
		{
			Scribe_Values.Look<bool>(ref this.throneRoomTweaks, "throneRoomTweaks", true, true);
			Scribe_Values.Look<bool>(ref this.spouseWantsThroneroom, "spouseWantsThroneroom", true, true);
			Scribe_Values.Look<bool>(ref this.willWorkPassionSkills, "willWorkPassionSkills", true, true);
			Scribe_Values.Look<bool>(ref this.willWorkOnlyMajorPassionSkills, "willWorkOnlyMajorPassionSkills", false, true);
			base.ExposeData();
		}

		// Token: 0x04000001 RID: 1
		public bool throneRoomTweaks;

		// Token: 0x04000002 RID: 2
		public bool spouseWantsThroneroom;

		// Token: 0x04000003 RID: 3
		public bool willWorkPassionSkills;

		// Token: 0x04000004 RID: 4
		public bool willWorkOnlyMajorPassionSkills;

		// Token: 0x04000005 RID: 5
		public float authorityFallPerDayMultiplier;
	}
	// Token: 0x02000004 RID: 4
	public class RoyaltyTweaksMod : Mod
	{
		// Token: 0x06000004 RID: 4 RVA: 0x000020C7 File Offset: 0x000002C7
		public RoyaltyTweaksMod(ModContentPack content) : base(content)
		{
			this.settings = base.GetSettings<RoyaltyTweaksSettings>();
		}

		// Token: 0x06000005 RID: 5 RVA: 0x000020DC File Offset: 0x000002DC
		public override void DoSettingsWindowContents(Rect inRect)
		{
			Listing_Standard listingStandard = new Listing_Standard();
			listingStandard.Begin(inRect);
			listingStandard.Label("-=[ " + Translator.Translate("ThroneRoomSettings") + " ]=-", -1f, null);
			listingStandard.CheckboxLabeled(Translator.Translate("ThroneRoomRequiredByHighestCheckboxLabel"), ref this.settings.throneRoomTweaks, Translator.Translate("ThroneRoomRequiredByHighestCheckboxTooltip"));
			if (this.settings.throneRoomTweaks)
			{
				listingStandard.CheckboxLabeled(Translator.Translate("SpouseOptionLabel"), ref this.settings.spouseWantsThroneroom, Translator.Translate("SpouseOptionTooltip"));
			}
			listingStandard.Label("", -1f, null);
			listingStandard.Label("-=[ " + Translator.Translate("RoyalPassionSkillSettings") + " ]=-", -1f, null);
			listingStandard.CheckboxLabeled(Translator.Translate("PassionSkillsCheckboxLabel"), ref this.settings.willWorkPassionSkills, Translator.Translate("PassionSkillsCheckboxTooltip"));
			if (this.settings.willWorkPassionSkills)
			{
				if (listingStandard.RadioButton(Translator.Translate("MajorAndMinorLabel"), !this.settings.willWorkOnlyMajorPassionSkills, 0f, Translator.Translate("MajorAndMinorTooltip"), null))
				{
					this.settings.willWorkOnlyMajorPassionSkills = false;
				}
				if (listingStandard.RadioButton(Translator.Translate("OnlyMajorLabel"), this.settings.willWorkOnlyMajorPassionSkills, 0f, Translator.Translate("OnlyMajorTooltip"), null))
				{
					this.settings.willWorkOnlyMajorPassionSkills = true;
				}
			}
			listingStandard.End();
			base.DoSettingsWindowContents(inRect);
		}

		// Token: 0x06000006 RID: 6 RVA: 0x000022AE File Offset: 0x000004AE
		public override string SettingsCategory()
		{
			return Translator.Translate("RoyalTweaks");
		}

		// Token: 0x04000006 RID: 6
		private RoyaltyTweaksSettings settings;
	}
}
