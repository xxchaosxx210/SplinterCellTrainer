using SCTrainer;
void Check(bool valid,string label) {if(!valid)throw new Exception(label);Console.WriteLine("PASS "+label);}
void Reject(string response,string label) {try{NpcSnapshot.Parse(response);}catch(FormatException){Console.WriteLine("PASS "+label);return;}throw new Exception(label);}
var pair=NpcSnapshot.Parse("NPCS:3\n1|1|0|1|1|100|50|2|Cop|DefaultState|s_Default|Pistol|None|1|0|0|0|Guard|0\n2|1|0|1|4|68|240|4|Cop|DefaultState|s_Alert|Pistol|Sam Fisher|1|0|0|0|Guard|0");
Check(pair.Length==2&&pair[0].Id!=pair[1].Id,"multiple independent identities");
Check(pair[0].Movement=="Walking"&&pair[1].Movement=="Running","native movement enum classification");
Check(pair[1].Activity=="Alerted"&&pair[1].Condition=="Alive","condition independent of activity");
var n=pair[0];
Check((n with {Health=0,PawnState="s_Unconscious",AiState="s_Unconscious"}).Condition=="Knocked out","unconscious is distinct from dead at zero health");
Check((n with {Health=0,PawnState="s_Dying",AiState="None",Controller=false}).Condition=="Dead","corpse without controller remains dead");
Check((n with {Health=0,PawnState="None",AiState="None"}).Condition=="Incapacitated","unknown zero health is not guessed dead");
Check(!(n with {PawnState="s_Unconscious"}).CanEditHealth,"unconscious actor cannot be healed into invalid state");
Check((n with {Present=false}).Condition=="Removed"&&!(n with {Present=false}).CanEditHealth,"removed identities cannot be edited");
Check((n with {Removing=true}).Condition=="Removing","scheduled expiry distinguished from removal");
Check(NpcSnapshot.Parse("NPCS:3").Length==0,"empty roster accepted");
Reject("NPCS:99","incompatible version rejected");
Reject("NPCS:3\n1|1|0","partial snapshot rejected");
Reject("NPCS:3\n1|1|0|1|1|100|NaN|2|Cop|None|None|None|None|1|0|0|0|Guard|0","nonfinite data rejected");
Reject("NPCS:3\n1|1|0|1|1|100|0|2|Cop|None|None|None|None|1|0|0|0|Guard|0\n1|1|0|1|1|100|0|2|Cop|None|None|None|None|1|0|0|0|Guard|0","duplicate identities rejected");

Check(n.CanSetCondition(NpcConditionEdit.KnockedOut)&&n.CanSetCondition(NpcConditionEdit.Dead)&&!n.CanSetCondition(NpcConditionEdit.Alive),"living NPC can be knocked out or killed, not revived");
var unconscious=n with {PawnState="s_Unconscious",AiState="s_Unconscious"};
Check(unconscious.CanSetCondition(NpcConditionEdit.Alive)&&unconscious.CanSetCondition(NpcConditionEdit.Dead)&&!unconscious.CanSetCondition(NpcConditionEdit.KnockedOut),"unconscious NPC can be revived or killed");
var dead=n with {Health=0,PawnState="s_Dying",AiState="None",Controller=false};
Check(!Enum.GetValues<NpcConditionEdit>().Any(dead.CanSetCondition),"corpse cannot be resurrected through condition editing");
Check(!(n with {Present=false}).CanSetCondition(NpcConditionEdit.Dead)&&!(n with {Removing=true}).CanSetCondition(NpcConditionEdit.KnockedOut),"absent and expiring identities cannot change condition");
Check((unconscious with {AiState="s_Groggy"}).Condition=="Reviving","recovery animation distinguished from completed revival");
Check(!(n with {Controller=false}).CanSetCondition(NpcConditionEdit.KnockedOut)&&!n.CanSetCondition((NpcConditionEdit)99),"missing controller and unknown conditions rejected");

Check(n.CanEditActivity,"living NPC in normal AI state permits activity editing");
Check(!unconscious.CanEditActivity&&!dead.CanEditActivity,"incapacitated NPC activity editing blocked");
Check(!(n with {Present=false}).CanEditActivity&&!(n with {Removing=true}).CanEditActivity,"stale and removing actors cannot receive goals");
Check(!(n with {AiState="s_ClimbLadder"}).CanEditActivity,"special interaction activity editing blocked");
Check((n with {AiState="s_Alert"}).CanEditActivity&&(n with {AiState="s_Investigate"}).CanEditActivity,"normal alert and search states can accept a replacement goal");

var map=n with {TrainerSpawned=false,InstanceName="PoliceGuard"};
Check(map.Origin=="Map NPC"&&!map.CanRemove&&map.CanEditPosition,"map NPC origin and native removal policy represented");
Check(n.CanRemove&&n.CanEditPosition,"trainer NPC permits removal and position edits");
Check(!(unconscious with {TrainerSpawned=false}).CanEditPosition&&!dead.CanEditPosition,"incapacitated actors cannot be repositioned");
var position=NpcPosition.Parse("-123.25","456.5","1800");
Check(position.X==-123.25f&&position.Y==456.5f&&position.IsValid,"negative and decimal game coordinates accepted");
Check(!new NpcPosition(float.NaN,0,0).IsValid&&!new NpcPosition(0,float.PositiveInfinity,0).IsValid&&!new NpcPosition(1000001,0,0).IsValid,"nonfinite and out-of-range destinations rejected");
Reject("NPCS:1","old snapshot protocol rejected");
var mapped=NpcSnapshot.Parse("NPCS:3\n7|1|0|1|1|100|0|20|Cop|DefaultState|s_Default|Pistol|None|0|-123.25|456.5|1800|PoliceGuard|0")[0];
Check(!mapped.TrainerSpawned&&mapped.InstanceName=="PoliceGuard"&&mapped.X==-123.25f,"map origin, identity and position parsed from native wire data");

Check(!(n with {Present=false}).ShowInRoster,"removed spawned NPC is hidden from the roster on every refresh");
Check((n with {Removing=true}).ShowInRoster,"pending removal remains visible until the game confirms absence");
Check(unconscious.ShowInRoster&&dead.ShowInRoster,"present unconscious and dead NPCs remain listed");
Check((map with {Present=false}).ShowInRoster,"map NPC history is preserved independently of spawned cleanup");

Check(NpcSnapshot.Parse("NPCS:3\n8|1|0|1|1|100|0|20|Cop|DefaultState|s_Default|Pistol|None|0|0|0|0|ProtectedGuard|1")[0].ProtectedGlobal,"native protection marker parsed for global exclusion");
Reject("NPCS:3\n8|1|0|1|1|100|0|20|Cop|DefaultState|s_Default|Pistol|None|0|0|0|0|ProtectedGuard|2","invalid protection marker rejected");
