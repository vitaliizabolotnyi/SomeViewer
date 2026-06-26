// Compiled at runtime by NVRTC (see Rendering/VolumeRenderer.cs).
// Output is tightly packed RGBA8 (4 bytes per pixel), so we avoid CUDA vector
// types that NVRTC would need extra headers for.

// Fallback test pattern: proves the CUDA -> mapped PBO -> texture path by
// writing an RGBA gradient. Used when no volume is loaded.
extern "C" __global__ void fillGradient(
	unsigned char* __restrict__ output,
	int width,
	int height)
{
	int x = blockIdx.x * blockDim.x + threadIdx.x;
	int y = blockIdx.y * blockDim.y + threadIdx.y;
	if (x >= width || y >= height) return;

	int idx = (y * width + x) * 4;
	output[idx + 0] = (unsigned char)(255 * x / width);   // R ramps left -> right
	output[idx + 1] = (unsigned char)(255 * y / height);  // G ramps bottom -> top
	output[idx + 2] = 128;                                 // B constant
	output[idx + 3] = 255;                                 // A opaque
}

// Animated plasma: four overlapping sine-wave fields driven by 'time' (seconds).
// Each pixel computes a weighted sum of sinusoidal surfaces, normalised to [0,1],
// then mapped through a three-phase colour palette (r/g/b shifted by 2pi/3) for
// smooth, continuously looping colour cycling.
//
// Formula breakdown:
//   w  = sin(6pi*u + t)                           horizontal standing wave
//      + sin(6pi*v + 1.5t)                        vertical standing wave
//      + sin(4pi*(u+v) + 0.8t)                    diagonal interference
//      + sin(18pi*dist(p, moving_centre) - 2.5t)  radial ripple from orbiting point
// The moving centre traces an ellipse: cx = 0.5 + 0.4*cos(0.7t),
//                                      cy = 0.5 + 0.4*sin(0.5t).
// w lies in [-4, 4]; it is remapped to [0,1] and fed into:
//   R = 0.5 + 0.5*sin(2pi*t')
//   G = 0.5 + 0.5*sin(2pi*t' + 2pi/3)
//   B = 0.5 + 0.5*sin(2pi*t' + 4pi/3)
extern "C" __global__ void animatedWaves(
	unsigned char* __restrict__ output,
	int width,
	int height,
	float time)
{
	int x = blockIdx.x * blockDim.x + threadIdx.x;
	int y = blockIdx.y * blockDim.y + threadIdx.y;
	if (x >= width || y >= height) return;

	// Normalised pixel coordinates in [0, 1].
	float u = (x + 0.5f) / width;
	float v = (y + 0.5f) / height;

	const float PI = 3.14159265f;

	// Four overlapping sine fields.
	float w = sinf(u * 6.0f * PI + time);
	w += sinf(v * 6.0f * PI + time * 1.5f);
	w += sinf((u + v) * 4.0f * PI + time * 0.8f);

	// Radial ripple from an orbiting centre.
	float cx   = 0.5f + 0.4f * cosf(time * 0.7f);
	float cy   = 0.5f + 0.4f * sinf(time * 0.5f);
	float dist = sqrtf((u - cx) * (u - cx) + (v - cy) * (v - cy));
	w += sinf(dist * 18.0f * PI - time * 2.5f);

	// Map [-4, 4] -> [0, 1].
	float t = (w + 4.0f) * 0.125f;

	// Three-phase sine colour palette: R, G, B offset by 2pi/3 each.
	float r = 0.5f + 0.5f * sinf(2.0f * PI * t);
	float g = 0.5f + 0.5f * sinf(2.0f * PI * t + 2.0943951f);   // 2pi/3
	float b = 0.5f + 0.5f * sinf(2.0f * PI * t + 4.1887902f);   // 4pi/3

	int idx = (y * width + x) * 4;
	output[idx + 0] = (unsigned char)(255.0f * r);
	output[idx + 1] = (unsigned char)(255.0f * g);
	output[idx + 2] = (unsigned char)(255.0f * b);
	output[idx + 3] = 255;
}

// Sample one axial slice of the uploaded volume.
// texture (single-channel float, normalized coords), so tex3D returns
// hardware-filtered densities in [0,1]. Used as a debug view of the upload and
// texture binding. cudaTextureObject_t is a built-in NVRTC type; tex3D<float> is
// a built-in device function.
extern "C" __global__ void sampleVolume(
	cudaTextureObject_t volume,
	unsigned char* __restrict__ output,
	int width,
	int height,
	float slice)
{
	int x = blockIdx.x * blockDim.x + threadIdx.x;
	int y = blockIdx.y * blockDim.y + threadIdx.y;
	if (x >= width || y >= height) return;

	// Map the screen pixel to the slice's [0,1] texture coordinates.
	float u = (x + 0.5f) / width;
	float v = (y + 0.5f) / height;

	float density = tex3D<float>(volume, u, v, slice);
	unsigned char g = (unsigned char)(255.0f * density);

	int idx = (y * width + x) * 4;
	output[idx + 0] = g;
	output[idx + 1] = g;
	output[idx + 2] = g;
	output[idx + 3] = 255;
}

// --- Direct volume rendering (raycasting) --------------------------------------

// Unproject a clip-space point (cx, cy, cz, 1) into volume-local space using the
// inverse model-view-projection matrix. The matrix is row-major (m[i*4+j] = M[i,j])
// and applied with the row-vector convention (local = clip * invMvp), matching the
// shader's "vec * model * view * projection". The perspective w-divide recovers the
// 3D position regardless of the unknown clip scale.
__device__ void unproject(
	const float* __restrict__ m,
	float cx, float cy, float cz,
	float* ox, float* oy, float* oz)
{
	float x = cx * m[0] + cy * m[4] + cz * m[8]  + m[12];
	float y = cx * m[1] + cy * m[5] + cz * m[9]  + m[13];
	float z = cx * m[2] + cy * m[6] + cz * m[10] + m[14];
	float w = cx * m[3] + cy * m[7] + cz * m[11] + m[15];
	float inv = 1.0f / w;
	*ox = x * inv;
	*oy = y * inv;
	*oz = z * inv;
}

// Slab-method ray/box intersection against the unit box [-0.5, 0.5]^3 that holds
// the volume in local space. Axis-aligned rays divide by zero -> +/-inf, which
// fminf/fmaxf resolve correctly. Returns the entry/exit ray parameters.
__device__ bool intersectBox(
	float ox, float oy, float oz,
	float dx, float dy, float dz,
	float* tNear, float* tFar)
{
	const float bmin = -0.5f;
	const float bmax = 0.5f;

	float t0x = (bmin - ox) / dx;
	float t1x = (bmax - ox) / dx;
	float tminx = fminf(t0x, t1x);
	float tmaxx = fmaxf(t0x, t1x);

	float t0y = (bmin - oy) / dy;
	float t1y = (bmax - oy) / dy;
	float tminy = fminf(t0y, t1y);
	float tmaxy = fmaxf(t0y, t1y);

	float t0z = (bmin - oz) / dz;
	float t1z = (bmax - oz) / dz;
	float tminz = fminf(t0z, t1z);
	float tmaxz = fmaxf(t0z, t1z);

	float tn = fmaxf(fmaxf(tminx, tminy), tminz);
	float tf = fminf(fminf(tmaxx, tmaxy), tmaxz);

	*tNear = tn;
	*tFar = tf;
	return tf >= tn && tf >= 0.0f;
}

// Cast one ray per pixel through the volume and composite front-to-back. The eye
// ray is built by unprojecting the pixel's near/far clip points through invMvp, so
// any camera/model transform just changes the matrix. Each step's density is
// window/leveled (winCenter/winWidth in normalized [0,1]) then mapped through the
// transfer-function LUT (flat RGBA, lutSize entries) to a color + base opacity;
// opacity is corrected for step size so the look is stable as stepSize changes.
extern "C" __global__ void raycastVolume(
	cudaTextureObject_t volume,
	const float* __restrict__ invMvp,
	const float* __restrict__ lut,
	int lutSize,
	unsigned char* __restrict__ output,
	int width,
	int height,
	float stepSize,
	float densityScale,
	float winCenter,
	float winWidth)
{
	int px = blockIdx.x * blockDim.x + threadIdx.x;
	int py = blockIdx.y * blockDim.y + threadIdx.y;
	if (px >= width || py >= height) return;

	int idx = (py * width + px) * 4;

	// Pixel center -> NDC [-1, 1]. The PBO's row 0 becomes the texture's bottom
	// row on upload, so we do NOT flip Y here -> orientation matches the slice sampler.
	float ndcX = 2.0f * (px + 0.5f) / width - 1.0f;
	float ndcY = 2.0f * (py + 0.5f) / height - 1.0f;

	// Eye ray in volume-local space: near plane (z=-1) to far plane (z=+1).
	float nx, ny, nz, fx, fy, fz;
	unproject(invMvp, ndcX, ndcY, -1.0f, &nx, &ny, &nz);
	unproject(invMvp, ndcX, ndcY,  1.0f, &fx, &fy, &fz);

	float dx = fx - nx;
	float dy = fy - ny;
	float dz = fz - nz;
	float dlen = sqrtf(dx * dx + dy * dy + dz * dz);
	dx /= dlen; dy /= dlen; dz /= dlen;

	float tNear, tFar;
	if (!intersectBox(nx, ny, nz, dx, dy, dz, &tNear, &tFar))
	{
		// Ray misses the volume box -> background.
		output[idx + 0] = 0;
		output[idx + 1] = 0;
		output[idx + 2] = 0;
		output[idx + 3] = 255;
		return;
	}

	if (tNear < 0.0f) tNear = 0.0f;

	float accumR = 0.0f;
	float accumG = 0.0f;
	float accumB = 0.0f;
	float accumAlpha = 0.0f;

	for (float t = tNear; t < tFar; t += stepSize)
	{
		// Local [-0.5, 0.5] -> texture [0, 1].
		float u = (nx + dx * t) + 0.5f;
		float v = (ny + dy * t) + 0.5f;
		float w = (nz + dz * t) + 0.5f;

		float sample = tex3D<float>(volume, u, v, w);

		// Window/level: remap [center - width/2, center + width/2] -> [0, 1].
		float remapped = (sample - (winCenter - 0.5f * winWidth)) / winWidth;
		remapped = fminf(fmaxf(remapped, 0.0f), 1.0f);

		// Map density through the transfer function (linearly interpolated LUT).
		float fpos = remapped * (lutSize - 1);
		int i0 = (int)fpos;
		if (i0 < 0) i0 = 0;
		if (i0 > lutSize - 1) i0 = lutSize - 1;
		int i1 = i0 < lutSize - 1 ? i0 + 1 : i0;
		float frac = fpos - i0;

		int o0 = i0 * 4;
		int o1 = i1 * 4;
		float srcR = lut[o0 + 0] + (lut[o1 + 0] - lut[o0 + 0]) * frac;
		float srcG = lut[o0 + 1] + (lut[o1 + 1] - lut[o0 + 1]) * frac;
		float srcB = lut[o0 + 2] + (lut[o1 + 2] - lut[o0 + 2]) * frac;
		float srcA = lut[o0 + 3] + (lut[o1 + 3] - lut[o0 + 3]) * frac;

		// Opacity correction so density maps consistently across step sizes.
		float alpha = 1.0f - expf(-srcA * densityScale * stepSize);
		float oneMinus = 1.0f - accumAlpha;

		// Front-to-back compositing with premultiplied color.
		accumR += oneMinus * alpha * srcR;
		accumG += oneMinus * alpha * srcG;
		accumB += oneMinus * alpha * srcB;
		accumAlpha += oneMinus * alpha;

		if (accumAlpha >= 0.99f) break;   // early ray termination
	}

	output[idx + 0] = (unsigned char)(255.0f * fminf(accumR, 1.0f));
	output[idx + 1] = (unsigned char)(255.0f * fminf(accumG, 1.0f));
	output[idx + 2] = (unsigned char)(255.0f * fminf(accumB, 1.0f));
	output[idx + 3] = 255;
}
