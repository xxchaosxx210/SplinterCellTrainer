#include "../../Native/MovePlacement.h"
#include <cstdio>
int main(){bool ok=true;auto check=[&](bool b,const char* label){std::printf("%s %s\n",b?"PASS":"FAIL",label);ok&=b;};
 check(!MovePlacement::Valid(2,0)&&!MovePlacement::Valid(7,0)&&MovePlacement::Valid(3,0)&&MovePlacement::Valid(6,255),"only three-to-six metres and bounded registry slots accepted");
 auto p=MovePlacement::Offset(3,0,0);check(p.x==300&&p.y==0,"single NPC moves directly three metres in front");
 auto left=MovePlacement::Offset(3,1,0),right=MovePlacement::Offset(3,2,0);check(left.x==300&&right.x==300&&left.y==120&&right.y==-120,"batch NPCs occupy distinct lateral row positions");
 auto q=MovePlacement::Offset(6,0,1.57079632679f);check(std::fabs(q.x)<0.01f&&std::fabs(q.y-600)<0.01f,"placement rotates with Sam's facing");
 check(!MovePlacement::FarEnough(299,0,0,3)&&MovePlacement::FarEnough(300,0,0,3),"collision adjustment cannot bring NPCs inside selected distance");
 check(!MovePlacement::FarEnough(NAN,0,0,3),"nonfinite engine positions rejected");
 return ok?0:1;}
