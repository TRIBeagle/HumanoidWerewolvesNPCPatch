using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace HumanoidWerewolvesNPCPatch
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "HNW_NPCWerewolfPatch.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            // ESL 플래그 설정
            state.PatchMod.ModHeader.Flags |= SkyrimModHeader.HeaderFlag.SmallFile;
            
            // FormKey 정의
            var werewolfBeastRaceKey = FormKey.Factory("0CDD84:Skyrim.esm");
            var hnwMainKey = ModKey.FromNameAndExtension("HNWMain.esp");

            // HNWMain.esp의 로드 순서 확인
            var hnwMainIndex = state.LoadOrder.IndexOf(hnwMainKey);
            if (hnwMainIndex == -1)
            {
                Console.WriteLine("\nHNWMain.esp not found in the load order.\n");
                return;
            }

            Console.WriteLine($"\nHNWMain.esp is at index: {hnwMainIndex}\n");

            // WerewolfBeastRace 검색: ContextOverrides 사용
            var werewolfBeastRaceContext = state.LinkCache.ResolveAllContexts<IRace, IRaceGetter>(werewolfBeastRaceKey)
                .Where(ctx =>
                {
                    var ctxIndex = state.LoadOrder.IndexOf(ctx.ModKey);
                    if (ctxIndex == -1)
                    {
                        Console.WriteLine($"Skipping ModKey: {ctx.ModKey} (not found in load order)");
                        return false;
                    }

                    Console.WriteLine($"Checking Race: {ctx.Record.EditorID}, ModKey: {ctx.ModKey}, Index: {ctxIndex}, HNWMainIndex: {hnwMainIndex}");
                    return ctxIndex < hnwMainIndex; // HNWMain.esp 이전의 레코드만 포함
                })
                .OrderByDescending(ctx => state.LoadOrder.IndexOf(ctx.ModKey)) // 가장 최근 수정된 플러그인 선택
                .FirstOrDefault();

            if (werewolfBeastRaceContext == null)
            {
                Console.WriteLine("\nFailed to find WerewolfBeastRace record prior to HNWMain.esp.\n");
                return;
            }

            var werewolfBeastRace = werewolfBeastRaceContext.Record;
            Console.WriteLine($"\nSuccessfully found WerewolfBeastRace in ModKey: {werewolfBeastRaceContext.ModKey}\n");

            // HNW_NPCWerewolfBeastRace를 HNWMain.esp에서 로드
            var hnwNpcWerewolfBeastRaceKey = FormKey.Factory("000836:HNWMain.esp");
            var hnwMainContext = state.LinkCache.Resolve<IRaceGetter>(hnwNpcWerewolfBeastRaceKey);
            if (hnwMainContext == null)
            {
                Console.WriteLine("Failed to find HNW_NPCWerewolfBeastRace in HNWMain.esp.\n");
                return;
            }

            // HNW_NPCWerewolfBeastRace 오버라이드를 생성하고 데이터 복사
            var hnwNpcWerewolfBeastRace = state.PatchMod.Races.GetOrAddAsOverride(hnwMainContext);
            hnwNpcWerewolfBeastRace.DeepCopyIn(werewolfBeastRace);

            // EditorID와 설명, Morph Race와 Armor Race 값 재설정
            hnwNpcWerewolfBeastRace.EditorID = "WerewolvesFailSafe"; // More Nasty Critters 인식용 ID
            hnwNpcWerewolfBeastRace.Description = "Werewolf Race for NPC";
            hnwNpcWerewolfBeastRace.MorphRace.SetTo(werewolfBeastRaceKey);
            hnwNpcWerewolfBeastRace.ArmorRace.SetTo(werewolfBeastRaceKey);
            Console.WriteLine("NPCWerewolfBeastRace EditorID, description, Morph Race, and Armor Race values have been updated.\n");

            // WerewolfBeastRace를 사용하는 NPC의 Race 변경 (HNWMain.esp 이전의 가장 최근 수정된 NPC만 반영)
            var npcsToPatch = state.LoadOrder.PriorityOrder.Npc()
                .WinningOverrides()
                .Select(npc =>
                {
                    var selectedNpcContext = state.LinkCache.ResolveAllContexts<INpc, INpcGetter>(npc.FormKey)
                        .Where(ctx => state.LoadOrder.IndexOf(ctx.ModKey) < hnwMainIndex)
                        .OrderByDescending(ctx => state.LoadOrder.IndexOf(ctx.ModKey))
                        .FirstOrDefault();

                    if (selectedNpcContext != null && selectedNpcContext.Record.Race?.FormKey == werewolfBeastRaceKey)
                    {
                        Console.WriteLine($"Selected NPC - EditorID: {selectedNpcContext.Record.EditorID}, FormID: {selectedNpcContext.Record.FormKey}, Last Modified Mod: {selectedNpcContext.ModKey}, Index: {state.LoadOrder.IndexOf(selectedNpcContext.ModKey)}");
                    }

                    return selectedNpcContext != null ? (selectedNpcContext.Record, selectedNpcContext.ModKey) : (null, ModKey.Null);
                })
                .Where(pair => pair.Item1 != null && pair.Item1.Race?.FormKey == werewolfBeastRaceKey)
                .ToList();

            Console.WriteLine($"\n{npcsToPatch.Count} NPCs have been selected. Starting the patch process.\n");

            foreach (var (npc, npcModKey) in npcsToPatch)
            {
                var patchedNpc = state.PatchMod.Npcs.GetOrAddAsOverride(npc);
                patchedNpc.Race.SetTo(hnwNpcWerewolfBeastRace.FormKey);

                var npcEditorID = npc.EditorID ?? "Unknown EditorID";
                var npcFormKey = npc.FormKey.ToString();
                var npcModIndex = state.LoadOrder.IndexOf(npcModKey);

                Console.WriteLine($"Patched NPC - EditorID: {npcEditorID}, FormID: {npcFormKey}, Last Modified Mod: {npcModKey}, Patched at Index: {npcModIndex}");
            }
            
            Console.WriteLine($"\nTotal {npcsToPatch.Count} NPCs have been patched.\n");
        }
    }
}
