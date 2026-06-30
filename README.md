# BE KingCom Dong Bo

Backend da tach theo kieu layered solution gan voi cau truc ASA:

```text
BE_KingCom_DongBo.csproj      API host: Program.cs, middleware, route mapping, Swagger
KingCom.Application/          Request/response contracts cho API
KingCom.Domain/               Entity/model/options thuan domain
KingCom.Infrastructure/       SQL, Auth store, Haravan client, sync service, logging, DI module
KingCom_DongBo.slnx           Solution mo 4 project tren Visual Studio / Rider
```

## Luong phu thuoc

```text
BE_KingCom_DongBo
  -> KingCom.Application
  -> KingCom.Domain
  -> KingCom.Infrastructure

KingCom.Infrastructure
  -> KingCom.Application
  -> KingCom.Domain

KingCom.Application
  -> KingCom.Domain
```

## Cau hinh

Secrets nam trong `.env`:

```text
ConnectionStrings__DefaultConnection
ConnectionStrings__AuthConnection
Inventory__StockQuery
Haravan__AccessToken
Haravan__LocationHcmId
Haravan__LocationHnId
Auth__Enabled
Jwt__Secret
Jwt__Issuer
Jwt__Audience
Jwt__ExpireMinutes
```

`.env` da duoc ignore. File mau la `.env.example`.

## Swagger

```text
http://127.0.0.1:5088/swagger
```

Login `POST /api/auth/login` tra ve JWT. Copy token vao Swagger nut `Authorize` theo dang:

```text
Bearer <token>
```
