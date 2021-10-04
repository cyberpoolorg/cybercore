#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

#include "Lyra2-z.h"

#include "sha3/sph_blake.h"

#define _ALIGN(x) __attribute__ ((aligned(x)))

extern uint64_t lyra2z_height;

void lyra2z_hash(const char* input, char* output, uint32_t len)
{
	uint32_t _ALIGN(64) hashB[8], hash[8];
	sph_blake256_context ctx_blake;
	sph_blake256_set_rounds(14);
	sph_blake256_init(&ctx_blake);
	sph_blake256(&ctx_blake, input, len);
	sph_blake256_close(&ctx_blake, hashB);

	LYRA2z(hash, 32, hashB, 32, hashB, 32, 8, 8, 8);

	memcpy(output, hash, 32);
}

