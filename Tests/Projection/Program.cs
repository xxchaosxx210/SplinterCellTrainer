using System;
using System.Numerics;
using SCTrainer;

void Check(bool pass, string name) { if (!pass) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); }
Check(EnemyProjection.TryProject(new Vector3(100,0,0), Vector3.Zero,0,0,0,90,800,600,out var x,out var y) && Math.Abs(x-400)<.01 && Math.Abs(y-300)<.01, "forward target centred");
Check(!EnemyProjection.TryProject(new Vector3(-100,0,0),Vector3.Zero,0,0,0,90,800,600,out x,out y), "behind-camera target hidden");
Check(EnemyProjection.TryProject(new Vector3(100,100,0),Vector3.Zero,0,0,0,90,800,600,out x,out y) && Math.Abs(x-800)<.01, "horizontal FOV edge");
Check(EnemyProjection.TryProject(new Vector3(0,100,0),Vector3.Zero,0,16384,0,90,800,600,out x,out y) && Math.Abs(x-400)<.01, "quarter-turn yaw");
Check(EnemyProjection.TryProject(new Vector3(100,0,100),Vector3.Zero,0,0,0,90,800,600,out x,out y) && y<300, "higher target above centre");
Check(!EnemyProjection.TryProject(new Vector3(100,0,0),Vector3.Zero,0,0,0,float.NaN,800,600,out x,out y), "invalid FOV rejected");
