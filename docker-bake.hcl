# docker buildx bake [target], from the repository root (deploy/scripts/Build-Artifact.ps1 calls it).

# The release workflow sets it from the tag (VERSION=1.2.0 docker buildx bake).
variable "VERSION" {
  default = "0.0.0-dev"
}

group "default" {
  targets = ["artifact"]
}

target "_artifact" {
  context    = "."
  dockerfile = "deploy/docker/Dockerfile"
  target     = "artifact"
  args       = { VERSION = VERSION }
}

# The LXC.
target "artifact" {
  inherits  = ["_artifact"]
  platforms = ["linux/amd64"]
  output    = ["type=local,dest=deploy/out/x64"]
}

# The Raspberry Pi 4.
target "artifact-arm64" {
  inherits  = ["_artifact"]
  platforms = ["linux/arm64"]
  output    = ["type=local,dest=deploy/out/arm64"]
}
