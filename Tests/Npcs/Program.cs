using SCTrainer;
void Check(bool valid,string label) {if(!valid)throw new Exception(label);Console.WriteLine("PASS "+label);}
void Reject(string response,string label) {try{NpcSnapshot.Parse(response);}catch(FormatException){Console.WriteLine("PASS "+label);return;}throw new Exception(label);}
var pair=NpcSnapshot.Parse("NPCS:1\n1|1|0|1|1|100|50|2|Cop|DefaultState|s_Default|Pistol|None\n2|1|0|1|4|68|240|4|Cop|DefaultState|s_Alert|Pistol|Sam Fisher");
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
Check(NpcSnapshot.Parse("NPCS:1").Length==0,"empty roster accepted");
Reject("NPCS:2","incompatible version rejected");
Reject("NPCS:1\n1|1|0","partial snapshot rejected");
Reject("NPCS:1\n1|1|0|1|1|100|NaN|2|Cop|None|None|None|None","nonfinite data rejected");
Reject("NPCS:1\n1|1|0|1|1|100|0|2|Cop|None|None|None|None\n1|1|0|1|1|100|0|2|Cop|None|None|None|None","duplicate identities rejected");
