#include "minotaurx.h"

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

extern "C" MODULE_API void minotaurx_export(const char* input, char* output, uint32_t input_len)
{
	minotaurx_hash(input, output, input_len);
}