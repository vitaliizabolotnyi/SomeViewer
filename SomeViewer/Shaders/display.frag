#version 330 core

in vec2 vTexCoord;

out vec4 outputColor;

uniform sampler2D screenTexture;

void main()
{
	outputColor = texture(screenTexture, vTexCoord);
}
