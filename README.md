Workflow与Activity分离

```bash
curl -X POST http://localhost:5101/start \
  -H "Content-Type: application/json" \
  -d '{"OrderId":"o-1001","ItemId":"sku-1","Qty":2,"Amount":199.00}'
```

```bash
curl http://localhost:5101/instances/o-1001
```