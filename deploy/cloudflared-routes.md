# Cloudflare tunnel routes for kroki-mcp

Two public hostnames terminate at Cloudflare's edge and proxy plain HTTP into the cluster.

## Add to `applications/cloudflared/configmap.yaml` (in the cluster ops repo)

```yaml
- hostname: kroki-mcp.donkeywork.dev
  service: http://kroki-mcp.kroki-mcp.svc.cluster.local:80

- hostname: kroki-cdn.donkeywork.dev
  service: http://seaweedfs.kroki-mcp.svc.cluster.local:8333
```

## Bind hostnames to the shared tunnel (run once)

```bash
cloudflared --origincert ~/.cloudflared/cert-donkeywork.pem tunnel route dns shared kroki-mcp.donkeywork.dev
cloudflared --origincert ~/.cloudflared/cert-donkeywork.pem tunnel route dns shared kroki-cdn.donkeywork.dev
```

## Roll cloudflared after editing the configmap

```bash
kubectl -n cloudflared rollout restart deployment/cloudflared
```

## Verifying directory listing is disabled

```bash
# Should return 403 (no s3:ListBucket for anonymous identity).
curl -i https://kroki-cdn.donkeywork.dev/diagrams/

# A specific known-key object returns 200 with image bytes.
curl -i https://kroki-cdn.donkeywork.dev/diagrams/<yyyy>/<mm>/<dd>/<uuid>.png
```
