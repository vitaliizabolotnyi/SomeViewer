// Compiled at runtime by NVRTC (see Window.OnLoad).
// extern "C" keeps the name un-mangled so it can be loaded as "WindowLevel".
extern "C" __global__ void WindowLevel(
	const short* __restrict__ input,
	unsigned char* __restrict__ output,
	int count,
	float windowCenter,
	float windowWidth)
{
	int i = blockIdx.x * blockDim.x + threadIdx.x;
	if (i >= count) return;

	float lo = windowCenter - 0.5f * windowWidth;
	float v  = (input[i] - lo) / windowWidth;   // normalize to [0,1]
	v = fminf(fmaxf(v, 0.0f), 1.0f);
	output[i] = (unsigned char)(v * 255.0f);
}
