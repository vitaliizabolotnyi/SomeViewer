#version 330 core

layout(location = 0) in vec3 aPosition;

out vec3 vColor;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

void main(void)
{
    // Local position in [-0.5, 0.5] mapped to [0, 1] so each face reads differently.
    vColor = aPosition + vec3(0.5);

    // Common/Shader.SetMatrix4 uploads with transpose = true, so the LearnOpenTK
    // vector-on-the-left convention applies here.
    gl_Position = vec4(aPosition, 1.0) * model * view * projection;
}
