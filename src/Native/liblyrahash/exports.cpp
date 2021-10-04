#include "Lyra2.h"
#include "Lyra2-z.h"
#include "lyra2re.h"
#include "lyra2v2.h"
#include "lyra2v3.h"
#include "lyra2vc0ban.h"
#include "lyra2z.h"
#include "lyra2z330.h"

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

extern "C" MODULE_API void lyra2re_export(const char* input, char* output, uint32_t input_len)
{
	lyra2re_hash(input, output, input_len);
}

extern "C" MODULE_API void lyra2rev2_export(const char* input, char* output, uint32_t input_len)
{
	lyra2v2_hash(input, output, input_len);
}

extern "C" MODULE_API void lyra2rev3_export(const char* input, char* output, uint32_t input_len)
{
	lyra2v3_hash(input, output, input_len);
}

extern "C" MODULE_API void lyra2vc0ban_export(const char* input, char* output, uint32_t input_len)
{
	lyra2vc0ban_hash(input, output, input_len);
}

extern "C" MODULE_API void lyra2z_export(const char* input, char* output, uint32_t input_len)
{
	lyra2z_hash(input, output, input_len);
}

extern "C" MODULE_API void lyra2z330_export(const char* input, char* output, uint32_t input_len)
{
	lyra2z330_hash(input, output, input_len);
}