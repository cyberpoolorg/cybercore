CC = gcc
CFLAGS = -g -Wall -c -fPIC -O2 -Wno-pointer-sign -Wno-deprecated-declarations -Wno-comment -Wno-incompatible-pointer-types -Wno-return-type -Wno-char-subscripts -Wno-unused-variable -Wno-uninitialized -Wno-unused-function -Wno-strict-aliasing -Wno-discarded-qualifiers -Wno-unused-const-variable -Wno-implicit-function-declaration -Wno-cpp -msse4 -msse4.1 -msse4.2 -mssse3 -mavx -mpclmul -maes
CXXFLAGS = -g -Wall -fPIC -fpermissive -O2 -Wno-char-subscripts -Wno-unused-variable -Wno-uninitialized -Wno-unused-function -Wno-strict-aliasing -Wno-sign-compare -Wno-cpp -std=c++11 -msse4 -msse4.1 -msse4.2 -mssse3 -mavx -mpclmul -maes
LDFLAGS = -shared
LDLIBS = -lsodium
TARGET  = libgrhash.so

OBJECTS = exports.o gost.o gr.o Lyra2.o Sponge.o \
          sha3/SWIFFTX.o sha3/aes_helper.o sha3/extra.o sha3/hamsi_helper.o \
          sha3/sph_blake.o sha3/sph_blake2s.o sha3/sph_bmw.o sha3/sph_cubehash.o sha3/sph_echo.o sha3/sph_fugue.o sha3/sph_groestl.o \
          sha3/sph_hamsi.o sha3/sph_haval.o sha3/sph_hefty1.o sha3/sph_jh.o sha3/sph_keccak.o sha3/sph_luffa.o sha3/sph_panama.o \
          sha3/sph_radiogatun.o sha3/sph_ripemd.o sha3/sph_sha2.o sha3/sph_sha2big.o sha3/sph_shabal.o sha3/sph_shavite.o \
          sha3/sph_simd.o sha3/sph_skein.o sha3/sph_sm3.o sha3/sph_tiger.o sha3/sph_whirlpool.o \
          cryptonote/crypto/aesb.o cryptonote/crypto/c_blake256.o cryptonote/crypto/c_groestl.o cryptonote/crypto/c_jh.o \
	  cryptonote/crypto/c_keccak.o cryptonote/crypto/c_skein.o cryptonote/crypto/hash.o cryptonote/crypto/oaes_lib.o \
	  cryptonote/cryptonight.o cryptonote/cryptonight_dark.o cryptonote/cryptonight_dark_lite.o cryptonote/cryptonight_fast.o \
	  cryptonote/cryptonight_lite.o cryptonote/cryptonight_soft_shell.o cryptonote/cryptonight_turtle.o cryptonote/cryptonight_turtle_lite.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	g++ $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	$(RM) $(TARGET) $(OBJECTS)
