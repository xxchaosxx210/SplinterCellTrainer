#include "../../Native/FreezePolicy.h"
#include <cstdio>
int main(){bool ok=true;auto check=[&](bool b,const char* label){std::printf("%s %s\n",b?"PASS":"FAIL",label);ok&=b;};
 check(FreezePolicy::RestoreBits(0xa5,0,4)==0xa1,"thaw clears only the freeze bit when originally off");
 check(FreezePolicy::RestoreBits(0xa1,4,4)==0xa5,"thaw preserves originally disabled AI");
 check(FreezePolicy::RestoreBits(0xf5,0,4)==0xf1,"unrelated flag changes survive thaw");
 check(!FreezePolicy::Expired(16000,1000),"heartbeat permits a fifteen-second grace period");
 check(FreezePolicy::Expired(16001,1000),"lost trainer heartbeat triggers restoration after the grace period");
 return ok?0:1;}
