#include "../../Native/NpcTrackingPolicy.h"
#include <cstdio>
int main(){int failures=0;
 auto check=[&](bool b,const char* label){std::printf("%s %s\n",b?"PASS":"FAIL",label);if(!b)++failures;};
 using NpcTrackingPolicy::CanBind;
 check(CanBind(true,true,false,123,123),"replacement controller binds to the same stable pawn");
 check(!CanBind(true,true,false,124,123),"another pawn's controller cannot be adopted");
 check(!CanBind(false,true,false,123,123),"destroyed or invalid controller cannot be adopted");
 check(!CanBind(true,false,false,123,123),"other-level controller rejected");
 check(!CanBind(true,true,true,123,123),"pending-deletion controller rejected");
 check(!CanBind(true,true,false,0,0),"missing reciprocal pawn rejected");
 return failures?1:0;}
