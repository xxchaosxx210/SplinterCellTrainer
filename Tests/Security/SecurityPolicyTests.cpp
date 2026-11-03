#include "../../Native/SecurityPolicy.h"
#include <cstdio>
int main(){bool ok=true;auto check=[&](bool b,const char* s){std::printf("%s %s\n",b?"PASS":"FAIL",s);ok&=b;};using namespace SecurityPolicy;
 check(Target(true,false,512)&&!Target(false,true,512),"camera toggle excludes turrets");
 check(Target(false,true,1024)&&!Target(true,false,1024),"turret toggle excludes cameras");
 check(Target(false,true,2048)&&!Target(true,false,2048),"IFF changes apply only to turrets");
 check(RestoreState(true,true,false),"sensor disabled by trainer can be restored");
 check(!RestoreState(false,true,false),"reused actor identities are never restored");
 check(!RestoreState(true,true,true),"destroyed or jammed devices are not reactivated");
 check(!RestoreState(true,false,false),"an externally changed sensor state is not overwritten during restore");
 check(RestoreIff(true,4,4)&&!RestoreIff(true,3,4)&&!RestoreIff(false,4,4),"IFF restores only a surviving owned setting");
 return ok?0:1;}
