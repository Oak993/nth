#include <stdint.h>
#include <dlfcn.h>
#include <stdlib.h>
/* Linux test environment only: forward Windows' unversioned ICU ABI to the real system ICU 76. */
static void *function(const char *name) {
  static void *library;
  if (!library) library = dlopen("libicuuc.so.76", RTLD_NOW | RTLD_LOCAL);
  void *result = library ? dlsym(library, name) : 0;
  if (!result) abort();
  return result;
}
int8_t u_hasBinaryProperty(int32_t cp, int32_t property) {
  return ((int8_t (*)(int32_t,int32_t))function("u_hasBinaryProperty_76"))(cp,property);
}
int32_t u_getIntPropertyValue(int32_t cp, int32_t property) {
  return ((int32_t (*)(int32_t,int32_t))function("u_getIntPropertyValue_76"))(cp,property);
}
#define CASE_FN(name) int32_t name(uint16_t *dest,int32_t cap,const uint16_t *src,int32_t len,const char *locale,int32_t *error) { return ((int32_t (*)(uint16_t*,int32_t,const uint16_t*,int32_t,const char*,int32_t*))function(#name "_76"))(dest,cap,src,len,locale,error); }
CASE_FN(u_strToLower)
CASE_FN(u_strToUpper)
int32_t u_strFoldCase(uint16_t *dest,int32_t cap,const uint16_t *src,int32_t len,uint32_t options,int32_t *error) {
 return ((int32_t (*)(uint16_t*,int32_t,const uint16_t*,int32_t,uint32_t,int32_t*))function("u_strFoldCase_76"))(dest,cap,src,len,options,error);
}
void *uidna_openUTS46(uint32_t options,int32_t *error) { return ((void *(*)(uint32_t,int32_t*))function("uidna_openUTS46_76"))(options,error); }
#define IDNA_FN(name) int32_t name(void *idna,const char *src,int32_t len,char *dest,int32_t cap,void *info,int32_t *error) { return ((int32_t (*)(void*,const char*,int32_t,char*,int32_t,void*,int32_t*))function(#name "_76"))(idna,src,len,dest,cap,info,error); }
IDNA_FN(uidna_nameToASCII_UTF8)
IDNA_FN(uidna_nameToUnicodeUTF8)
