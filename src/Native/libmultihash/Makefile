CFLAGS = -g -Wall -c -fPIC -O2 -Wno-pointer-sign -Wno-char-subscripts -Wno-unused-variable -Wno-uninitialized -Wno-return-type -Wno-implicit-function-declaration -Wno-unused-function -Wno-strict-aliasing -Wno-discarded-qualifiers -Wno-int-conversion -Wno-unused-const-variable -Wno-cpp -msse4 -msse4.1 -msse4.2 -mssse3 -mavx -mpclmul -maes
CXXFLAGS = -g -Wall -fPIC -fpermissive -O2 -faligned-new -Wno-char-subscripts -Wno-unused-variable -Wno-uninitialized -Wno-unused-function -Wno-strict-aliasing -Wno-sign-compare -std=c++11 -Wno-cpp -msse4 -msse4.1 -msse4.2 -mssse3 -mavx -mpclmul -maes
LDFLAGS = -shared
LDLIBS = -lsodium
TARGET  = libmultihash.so

OBJECTS = bcrypt.o blake.o c11.o dcrypt.o exports.o fresh.o fugue.o geek.o groestl.o hefty1.o jh.o keccak.o KeccakP-800-reference.o \
	lane.o minotaur.o neoscrypt.o nist5.o phi2.o phi5.o quark.o qubit.o s3.o scryptn.o sha256.o sha256-d.o shavite3.o skein.o Sponge.o  \
	x11.o x11evo.o x11k.o x11kvs.o x12.o x13.o x14.o x15.o x16r.o x16rt.o x16rv2.o x16s.o x17.o x17r.o x18.o x20r.o x21s.o x22.o x22i.o x25x.o \
	blake2/sse/blake2s.o blake2/sse/blake2b.o \
	equi/util.o equi/support/cleanse.o equi/random.o equi/uint256.o equi/arith_uint256.o equi/crypto/hmac_sha512.o\
	equi/crypto/sha1.o equi/crypto/sha512.o equi/crypto/sha256.o equi/crypto/hmac_sha256.o equi/crypto/equihash.o \
	equi/crypto/ripemd160.o equi/equihashverify.o \
	heavyhash/heavyhash.o heavyhash/keccak_tiny.o \
	sha3/aes_helper.o sha3/extra.o sha3/gost_streebog.o sha3/hamsi_helper.o sha3/SWIFFTX.o \
	sha3/sph_blake.o sha3/sph_bmw.o sha3/sph_cubehash.o sha3/sph_echo.o sha3/sph_fugue.o sha3/sph_groestl.o \
	sha3/sph_hamsi.o sha3/sph_haval.o sha3/sph_hefty1.o sha3/sph_jh.o sha3/sph_keccak.o sha3/sph_luffa.o sha3/sph_panama.o \
	sha3/sph_radiogatun.o sha3/sph_ripemd.o sha3/sph_sha2.o sha3/sph_sha2big.o sha3/sph_shabal.o sha3/sph_shavite.o \
	sha3/sph_simd.o sha3/sph_skein.o sha3/sph_sm3.o sha3/sph_tiger.o sha3/sph_whirlpool.o \
	verthash/tiny_sha3/sha3.o verthash/h2.o \
        verushash/haraka.o verushash/haraka_portable.o verushash/utilstrencodings.o \
        verushash/verus_hash.o verushash/verus_clhash.o verushash/verus_clhash_portable.o \
        yescrypt/sha256_Y.o yescrypt/yescrypt.o yescrypt/yescrypt-opt.o \
        yespower/yespower-blake2b.o yespower/blake2b-yp.o yespower/yespower-combined.o yespower/yespower-platform.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	$(CXX) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	$(RM) $(TARGET) $(OBJECTS)
