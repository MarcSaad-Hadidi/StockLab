# StockLab

StockLab est une plateforme d’apprentissage des marchés boursiers fondée sur l’investissement simulé. Elle associe une interface React, une API ASP.NET Core avec persistance SQL Server/Azure SQL et un module Python indépendant pour les modèles, les signaux et le backtesting.

Les comptes, le profil, les ordres de paper trading, les portefeuilles, l’historique et la watchlist sont connectés au backend. L’interface AI Trader consulte aussi ses API de lecture. Le projet n’envoie aucun ordre à un courtier réel.

L’état ci-dessous reflète le code fusionné dans `develop` au **23 octobre 2026**. Une fonctionnalité implémentée nécessite encore sa configuration de déploiement : sa présence dans le dépôt ne prouve ni la disponibilité d’Azure/AWS ni l’exécution d’un bot autonome.

## Sommaire

- [Fonctionnement de StockLab](#fonctionnement-de-stocklab)
- [État actuel](#état-actuel)
- [Stack technique](#stack-technique)
- [Architecture](#architecture)
- [Price alert monitoring](#price-alert-monitoring)
- [Structure du dépôt](#structure-du-dépôt)
- [Prérequis](#prérequis)
- [Installation locale](#installation-locale)
- [Configuration et secrets](#configuration-et-secrets)
- [Commandes utiles](#commandes-utiles)
- [Intégration continue](#intégration-continue)
- [Workflow de développement](#workflow-de-développement)
- [Sécurité](#sécurité)
- [Limites et feuille de route](#limites-et-feuille-de-route)
- [Contribution](#contribution)
- [Licence](#licence)

## Fonctionnement de StockLab

Le produit aide l’utilisateur à explorer les données de marché et à s’exercer à prendre des décisions de portefeuille sans envoyer d’ordres à un courtier.

- **Marché** : recherche, cotations, historique OHLCV, valeurs en mouvement, fondamentaux, résultats et actualités. Le fournisseur `Mock` est sélectionné par défaut pour le développement ; les adaptateurs externes nécessitent leurs clés et leurs budgets.
- **Compte** : inscription, connexion JWT, déconnexion locale, identité du top bar, lecture/modification du nom et de l’email, changement réel du mot de passe. Les pages affichent les erreurs de validation et les échecs de l’API.
- **Paper trading personnel** : préparation et confirmation d’un achat ou d’une vente, exécution simulée, mise à jour atomique du cash et des positions, historique et valorisation. Les données privées sont liées à l’utilisateur authentifié.
- **Watchlist** : consultation, ajout et suppression des symboles enregistrés pour le compte.
- **AI Trader** : consultation du portefeuille système, des décisions originales et des rejets du risque, des transactions, des performances, des modèles et des backtests. `--run-ai-decisions` produit les décisions versionnées (#257) ; `--execute-ai-decisions` applique le risque et exécute les décisions explicitement désignées en paper trading, avec journal des tentatives (#258). `--capture-ai-performance` et le worker optionnel `AiPerformanceCycle` enregistrent la valorisation et le contexte des trades/modèles par créneau UTC (#259). La page ne déclenche ni entraînement ni ordre ; l'enchaînement automatique inférence/exécution reste une configuration opérationnelle distincte.
- **Alertes** : API de gestion et surveillance périodique présentes. L’écran frontend n’est pas encore connecté et le passage persistant à l’état `Triggered` reste à implémenter.

## État actuel

| Fonctionnalité | État livré et limites |
| --- | --- |
| Authentification et compte | Login/Register reliés à l’API, session JWT, profil GET/PUT, changement du mot de passe avec vérification de l’ancien et confirmation de sauvegarde réelle. OAuth Google et récupération de mot de passe non implémentés. |
| Trading personnel | Ordres BUY/SELL `market` et `limit`, contrôles de cash/positions, confirmation, déduplication par `orderId`, persistance transactionnelle et gestion des changements de session. |
| Dashboard, Portfolio, Transactions | Données du compte, performance avec cotations, transactions récentes et historique filtré/paginé. Chargement, absence de données et erreurs restent des états distincts. |
| Watchlist | API et intégration frontend livrées. |
| Alertes de prix | CRUD privé et monitoring livrés ; intégration frontend (#57) et déclenchement persistant (#43) restants. |
| AI Trader backend | Portefeuille système indépendant, Risk Manager, exécution simulée, historiques persistés et calcul des métriques. Treize endpoints GET protégés, capture périodique optionnelle et contexte historique des trades/modèles ; aucune route d’exécution. |
| AI Trader frontend | Sept sections connectées aux API de lecture (#59/#241), avec états de chargement, erreurs, données indisponibles et rafraîchissement manuel. Le résumé Dashboard (#251) lit les performances réelles et se réactualise au retour sur la page. Les services et exports privés doivent être configurés. |
| ML | Ingestion/nettoyage/features, régression logistique et Random Forest, évaluation, signaux BUY/SELL/HOLD avec confiance, backtesting et comparaison Buy & Hold livrés. |
| Cycle de vie des modèles | Registre de versions, artefacts locaux/S3, réentraînement et promotion explicite livrés. Un candidat réentraîné reste `Archived` tant qu’il n’a pas été promu. |
| Persistance et CI | Modèles EF Core, migrations SQL, tests hors ligne et job CI SQL Server LocalDB avec contrôle des cas réellement exécutés. |

Les interfaces disposent de traductions française et anglaise et de mises en page responsives. Les limites de valorisation et les travaux cloud restants sont détaillés dans la [feuille de route](#limites-et-feuille-de-route).

## Stack technique

| Domaine | Technologie | État dans ce dépôt |
| --- | --- | --- |
| Frontend | React 19, TypeScript 6, Vite 8, i18next, MUI / MUI X Charts | Application multi-pages et clients API |
| API backend | ASP.NET Core sur .NET 10, C#, JWT Bearer, OpenAPI | Contrôleurs, validation, services métier et autorisation |
| Données de marché | Twelve Data, Alpha Vantage, fournisseur local simulé | Le mode simulé est utilisé par défaut ; les adaptateurs externes sont facultatifs |
| Apprentissage automatique | Python 3.12, NumPy, pandas, scikit-learn, requests, joblib, boto3 | Modèles, backtests, versionnement et artefacts |
| Persistance relationnelle | EF Core 10, SQL Server / Azure SQL | Migrations et services de compte, trading et AI Trader implémentés |
| Tests et automatisation | Node test runner / jsdom, xUnit, pytest, GitHub Actions | Suites frontend, backend, SQL Server et ML séparées |
| Stockage des modèles | Fichiers locaux, adaptateur AWS S3 privé | Implémenté ; ressources et autorisations AWS externes requises |
| Réentraînement planifié | CLI/scheduler Python ; modèle AWS EventBridge Scheduler → ECS Fargate + EFS | Exécution locale implémentée ; modèle cloud fourni, déploiement distinct |
| Messagerie et autres traitements cloud | Azure Service Bus, AWS SQS / Lambda | Intégrations applicatives encore prévues |

Les versions exactes sont définies dans les manifests et fichiers de verrouillage. Le stockage S3 des modèles ne signifie pas que tous les usages S3 du projet, les files de messages ou les traitements Lambda sont intégrés.

## Architecture

Le frontend passe par l’API ; il n’accède directement ni à SQL, ni à Python, ni à S3 :

    Navigateur / frontend React multi-pages
        └── HTTP /api (proxy Vite en développement)
              └── StockLab.Api
                    ├── StockLab.Application / StockLab.Domain
                    └── StockLab.Infrastructure
                          ├── EF Core → SQL Server / Azure SQL
                          ├── Marché : Mock / Twelve Data / Alpha Vantage
                          └── Lecture d’exports privés de métadonnées ML

    Module Python indépendant (ml/)
        └── Données → modèles → signaux / backtests / registre / artefacts
              └── Fichiers locaux ou S3 privé selon configuration

`StockLab.Domain` porte les entités et règles métier sans dépendance ASP.NET/EF. `StockLab.Application` expose les services et contrats ; `StockLab.Infrastructure` implémente la persistance, l’identité, le trading et les fournisseurs. `StockLab.Api` assemble l’injection de dépendances, les contrôleurs et les middlewares.

Le frontend utilise Vite en mode multi-pages. Le [registre des routes](frontend/src/navigation/routes.ts) associe les URL lisibles aux entrées HTML. En développement et en prévisualisation, le proxy `/api` cible `http://localhost:5274`. Un déploiement du build statique doit fournir ses propres règles de routage et d’accès à l’API.

Les exports ML lus par l’API sont privés et gérés par l’opérateur. Leur synchronisation et le cycle automatique inférence → risque → exécution → capture de valorisation ne sont pas orchestrés par la page AI Trader. Voir les [prérequis opérationnels AI Trader](frontend/AI_TRADER_OPERATIONS.md).

## Price alert monitoring

Le backend surveille automatiquement les alertes `Active`, toutes les minutes par
défaut. `PriceAlertMonitoring:Interval` configure l'intervalle (par exemple
`"00:01:00"` dans `backend/StockLab.Api/appsettings.json` ou via
`PriceAlertMonitoring__Interval`). La valeur est validée au démarrage.

- Au plus une quote par symbole et par cycle, partagée entre tous les utilisateurs
  et toutes les conditions, via le pipeline de cache, déduplication et limitation du débit.
- `Above` compare strictement `prix > cible`; `Below` compare strictement `prix < cible`.
  L'égalité ne produit aucun match; les devises doivent correspondre.
- Le worker attend le premier intervalle, crée un scope par cycle et ne lance
  jamais deux cycles simultanément. Les quotes manquantes ou en erreur sont isolées par symbole.
- #42 retourne des `PriceAlertMatch` avec l'heure d'observation de la quote et
  ne persiste pas encore `Triggered`. La mutation atomique appartient à #43.

Le fournisseur `Mock` reste le défaut hors ligne. Avec un fournisseur externe,
`PriceAlertMonitoring:DailyQuoteBudget` limite le monitoring à 200 lookups par jour
UTC par défaut, tous utilisateurs et symboles confondus. Après épuisement, les
symboles restants sont reportés au prochain jour UTC, en reprenant après le dernier
symbole traité. Les erreurs, quotes absentes et hits de cache comptent aussi.
La limite, strictement positive, est configurable et stockée en mémoire par instance;
un redémarrage la réinitialise. Ce budget laisse les requêtes foreground indépendantes
et ne remplace pas le quota du fournisseur. Sans alerte active, aucun appel de
données de marché n'est effectué. Les symboles sont nettoyés avant journalisation.

## Structure du dépôt

    StockLab/
    ├── .github/                         # Modèle de pull request et automatisations GitHub
    ├── backend/
    │   ├── StockLab.sln
    │   ├── StockLab.Api/                # API HTTP, contrôleurs, middleware, DI
    │   ├── StockLab.Application/        # Interfaces, DTO et contrats applicatifs
    │   ├── StockLab.Domain/             # Entités et règles du domaine
    │   ├── StockLab.Infrastructure/     # EF/migrations, identité, trading, fournisseurs
    │   ├── StockLab.UnitTests/          # Tests .NET, SQLite et SQL Server
    │   ├── README.md                    # Documentation spécifique au backend
    │   └── database-schema.md           # Contrat du modèle relationnel
    ├── frontend/
    │   ├── src/                         # Pages React, layout partagé et clients API
    │   ├── tests/                       # Tests TypeScript et tests de routage
    │   ├── public/                      # Icônes statiques et ressources publiques
    │   ├── *.html et */index.html       # Entrées multi-pages Vite
    │   ├── package.json                 # Scripts et dépendances frontend
    │   ├── AI_TRADER_OPERATIONS.md      # Prérequis et limites de l’intégration AI Trader
    │   └── README.md                    # Documentation du frontend
    ├── ml/
    │   ├── src/stocklab_ml/             # Package Python
    │   ├── tests/                       # Suite pytest hors ligne
    │   ├── data/raw/                    # Données locales ignorées après ingestion
    │   ├── data/processed/              # Données locales nettoyées ignorées
    │   ├── models/                      # Artefacts locaux de modèles ignorés
    │   ├── aws/                         # Conteneur et modèle de réentraînement cloud
    │   └── README.md                    # Configuration ML et contrats de données
    ├── docs/                            # Notes de conception et d’ingénierie
    └── README.md                        # Vue d’ensemble du projet

## Prérequis

Installez les éléments suivants avant de commencer le développement local :

- Git.
- Node.js **24.x** et npm, comme dans la CI, pour le frontend et ses tests.
- SDK .NET 10.
- Une base SQL Server ou Azure SQL accessible pour les fonctions de compte et de trading ; SQL Server Express LocalDB sous Windows pour les tests SQL locaux.
- Python 3.12.x, pip et le module standard venv pour le travail ML.
- PowerShell sous Windows ou un shell POSIX sous macOS/Linux.

Les dépendances frontend, backend et ML s’installent séparément. Le frontend AI Trader n’a pas besoin d’un processus Python local : il interroge l’API .NET. Les tests métier ordinaires utilisent des bases isolées et ne nécessitent pas de secrets cloud.

## Installation locale

Clonez le dépôt et travaillez depuis sa racine :

    git clone https://github.com/MarcSaad-Hadidi/StockLab.git
    cd StockLab

### Frontend

Installez les dépendances et démarrez le serveur de développement Vite :

    npm --prefix frontend ci
    npm --prefix frontend run dev -- --host 127.0.0.1

Ouvrez [http://127.0.0.1:5173/](http://127.0.0.1:5173/) lorsque Vite a démarré ; si le port est occupé, utilisez celui indiqué dans le terminal. La racine redirige vers `/login`. Les autres routes comprennent `/register`, `/market`, `/dashboard`, `/portfolio`, `/transactions`, `/watchlist`, `/alerts`, `/ai-trader` et `/profile`.

Login et Register utilisent réellement le backend. Une interface démarrée seule ne suffit pas à créer un compte ou à charger les données privées : configurez aussi l’API et SQL ci-dessous.

Pour générer un build de production et le prévisualiser localement :

    npm --prefix frontend run build
    npm --prefix frontend run preview -- --host 127.0.0.1

L’ancien build Dashboard reste disponible si nécessaire :

    npm --prefix frontend run build:dashboard
    npm --prefix frontend run preview -- --config vite.dashboard.config.ts --host 127.0.0.1

### API backend

**Avant le démarrage**, configurez les secrets locaux avec vos propres valeurs. Les placeholders suivants doivent être remplacés et ne constituent pas une configuration utilisable :

    dotnet user-secrets set "Jwt:SigningKey" "<RANDOM_SECRET_AT_LEAST_32_BYTES>" --project backend/StockLab.Api/StockLab.Api.csproj
    dotnet user-secrets set "ConnectionStrings:StockLab" "<PRIVATE_SQL_SERVER_CONNECTION_STRING>" --project backend/StockLab.Api/StockLab.Api.csproj

La clé JWT est validée au démarrage. La connexion SQL est nécessaire à la résolution des services de persistance ; les routes de marché seules peuvent fonctionner sans base. Le fournisseur `Mock` évite les clés de marché, mais ne remplace pas la base des comptes.

Restaurez les outils et les packages :

    dotnet tool restore
    dotnet restore backend/StockLab.sln

Sur la base de développement prévue à cet effet, examinez les migrations puis appliquez-les explicitement :

    dotnet ef migrations list --project backend/StockLab.Infrastructure/StockLab.Infrastructure.csproj --startup-project backend/StockLab.Api/StockLab.Api.csproj
    dotnet ef migrations script --project backend/StockLab.Infrastructure/StockLab.Infrastructure.csproj --startup-project backend/StockLab.Api/StockLab.Api.csproj
    dotnet ef database update --project backend/StockLab.Infrastructure/StockLab.Infrastructure.csproj --startup-project backend/StockLab.Api/StockLab.Api.csproj

L’API n’applique pas les migrations au démarrage. Voir la [gestion des migrations](backend/README.md#managing-migrations) avant une intervention sur une base existante.

Compilez et lancez le profil HTTP dans un second terminal :

    dotnet build backend/StockLab.sln --no-restore
    dotnet run --project backend/StockLab.Api/StockLab.Api.csproj --launch-profile http

L’API écoute sur http://localhost:5274. Le serveur Vite frontend relaie les requêtes /api vers cette adresse. Le profil HTTPS utilise https://localhost:7247 et peut nécessiter un certificat de développement local.

Vérifiez que l’API fonctionne :

    curl http://localhost:5274/health

En environnement `Development`, OpenAPI est disponible à `/openapi/v1.json`. Les principales familles de routes sont :

| Méthode | Route | Fonction |
| --- | --- | --- |
| GET | /health | Contrôle de santé |
| GET | `/api/stocks/search`, `/api/market/movers` | Recherche et valeurs en mouvement |
| GET | `/api/stocks/{symbol}/quote`, `/api/stocks/{symbol}/history` | Cotation et historique OHLCV |
| GET | `/api/stocks/{symbol}/fundamentals`, `/api/stocks/{symbol}/logo`, `/api/stocks/{symbol}/earnings`, `/api/stocks/{symbol}/news` | Enrichissement |
| POST | /api/auth/register | Créer un compte et son portefeuille initial |
| POST | /api/auth/login | Vérifier les identifiants et recevoir un jeton Bearer |
| GET / PUT | /api/profile | Lire ou modifier le nom et l’email |
| PUT | /api/auth/password | Changer le mot de passe après validation de l’ancien |
| GET | /api/portfolio | Lire le cash, les positions et les valeurs au coût d’acquisition du compte authentifié |
| GET | /api/portfolio/performance | Valorisation et performance avec les cotations du fournisseur configuré |
| POST | /api/portfolio/trades | Exécuter un ordre de paper trading personnel |
| GET | `/api/portfolio/transactions`, `/api/portfolio/transactions/history` | Transactions récentes et historique paginé |
| GET / POST / DELETE | /api/watchlist et /api/watchlist/{symbol} | Liste, ajout et suppression de symboles |
| GET / POST / PUT / DELETE | /api/alerts et /api/alerts/{id} | Gestion des alertes ; désactivation par POST /api/alerts/{id}/disable |
| GET | /api/ai-trader/* | Treize lectures protégées : portefeuille, performances/cycles, décisions, transactions, modèles et backtests |

Après connexion, les routes privées utilisent `Authorization: Bearer <token>`. La session frontend est conservée dans `localStorage`, vérifiée à l’expiration et actualisée lors des changements de session. La déconnexion efface la session locale. Chaque requête privée vérifie aussi la version de session du compte en base : un changement de mot de passe révoque tous ses anciens JWT, y compris celui de la session courante. Une nouvelle connexion avec le nouveau mot de passe fournit un token valide. Appliquer la migration `AddUserSessionVersion` avant le déploiement ; les sessions émises avant cette évolution doivent se reconnecter une fois.

#### Portfolio API

`GET /api/portfolio` lit le portefeuille de l’utilisateur connecté. Aucun identifiant
utilisateur ou portefeuille n’est accepté pour sélectionner un autre compte.

```http
GET /api/portfolio
Authorization: Bearer <token>
```

La réponse contient `cashBalance`, `investedValue`, `totalValue`, `currency` et
`positions` (`symbol`, `quantity`, `averageCost`). Pour un nouveau compte :
100 000 USD de cash, aucune position et une valeur totale de 100 000 USD.
La valeur investie est la somme des quantités multipliées par leurs prix moyens ;
la valeur totale est le cash plus cette valeur investie, au coût d’acquisition.
`GET /api/portfolio/performance` fournit séparément la valorisation avec les cotations et la performance par rapport au capital initial, gains réalisés compris. Les produits de valorisation sont arrondis à douze décimales par position avant agrégation. La page Portfolio enrichit ses positions avec les cotations, dans la limite actuelle de vingt symboles par chargement. Si une cotation manque ou si ce plafond est dépassé, la valorisation totale reste indisponible ; le coût d’acquisition n’est pas présenté comme un prix de marché.

Les réponses documentées sont 200, 401, 404 (`portfolio_not_found`) et 500.
Une lecture ne crée jamais de portefeuille manquant. Voir le
[contrat détaillé et les tests](backend/README.md#portfolio-api).

#### Ordres et précision comptable

Les ordres personnels `market` et `limit` sont évalués lors de la soumission. Une limite non atteinte produit un rejet : elle ne crée pas d’ordre en attente surveillé en arrière-plan. Le backend vérifie le cash disponible, la quantité détenue et les limites de stockage, puis enregistre cash, position et transaction atomiquement.

La quantité est normalisée à huit décimales et le prix d’exécution à quatre. Leur produit exact conserve jusqu’à douze décimales ; `CashBalance` et `TotalAmount` utilisent `decimal(27,12)`. Les montants et opérandes décimaux concernés traversent les API personnelles sous forme de chaînes, afin d’éviter une conversion préalable en `number` JavaScript. L’estimation et la confirmation utilisent cette précision. Les règles de règlement du portefeuille AI sont distinctes et décrites dans la [documentation backend](backend/README.md#ai-paper-trade-execution-68).

L’`orderId` rend une soumission identique rejouable sans seconde exécution. Après une réponse perdue, la récupération du même compte conserve cet identifiant ; préparer un nouvel ordre ne constitue pas une vérification du précédent. La déduplication et les contrôles de concurrence sont couverts par les tests backend/SQL.

#### Alertes et AI Trader

L’[API Price Alerts](backend/README.md#price-alerts-api-41) isole les alertes par utilisateur, valide les conditions `Above`/`Below` en USD et gère les conflits de concurrence. Son écran frontend reste non connecté.

Les [onze routes AI Trader](backend/README.md#ai-trader-http-api-80) exposent un portefeuille système partagé entre utilisateurs authentifiés, séparé des portefeuilles personnels. Les GET ne créent pas de capital, n’exécutent aucun ordre et ne capturent pas de point de performance. Les montants, prix et quantités sont des chaînes décimales ; les taux sont des fractions numériques (`0.12` signifie 12 %).

La page exige un portefeuille AI préalablement initialisé, les migrations correspondantes, les cotations nécessaires et des exports ML privés configurés. En production, les endpoints de valorisation AI refusent le fournisseur `Mock`. Un catalogue absent renvoie une indisponibilité, pas une liste vide inventée. Les graphiques portent sur les observations persistées chargées, au plus les 200 dernières, sans garantir un historique complet. Voir les [prérequis opérationnels et limites de vérification](frontend/AI_TRADER_OPERATIONS.md).

### Module ML (facultatif)

Créez un environnement Python isolé uniquement lorsque vous travaillez sur ml/ :

    cd ml
    python3.12 -m venv .venv

Sous Windows PowerShell, utilisez py -3.12 -m venv .venv si nécessaire, puis activez l’environnement avec .\.venv\Scripts\Activate.ps1. Sous macOS/Linux, utilisez source .venv/bin/activate. Installez ensuite les dépendances et lancez la suite hors ligne :

    python -m pip install --upgrade pip
    python -m pip install -r requirements.txt
    python -m pytest

Le module inclut désormais les deux familles de modèles, les signaux avec confiance, le backtesting, le benchmark, le registre, le stockage d’artefacts, le réentraînement et la promotion. Aucun de ces composants Python n’envoie directement d’ordre à un courtier ou au portefeuille personnel.

Consultez le [README ML](ml/README.md) pour les commandes et contrats propres à chaque étape, les limites d’ingestion, les règles de stockage et les configurations facultatives.

## Configuration et secrets

Le dépôt peut être cloné sans identifiants. Ne versionnez jamais de vraies valeurs dans appsettings*.json, les fichiers .env du frontend, les fichiers Python, les exemples de commandes ou les tests.

### Variables frontend

| Variable | Fonction | Valeur par défaut |
| --- | --- | --- |
| VITE_STOCKLAB_API_BASE_URL | URL absolue facultative de l’API utilisée par le frontend | Vide : /api utilise le proxy Vite |

Les variables frontend sont intégrées au code du navigateur. Elles ne doivent jamais contenir de clés fournisseur ni d’autres secrets.

### Configuration backend

La configuration Development versionnée sélectionne le fournisseur hors ligne :

    {
      "MarketData": { "Provider": "Mock" }
    }

Pour les secrets locaux, préférez les user-secrets .NET depuis la racine du dépôt :

    dotnet user-secrets set "TwelveData:Keys:Website" "<TWELVE_DATA_WEBSITE_KEY>" --project backend/StockLab.Api/StockLab.Api.csproj
    dotnet user-secrets set "TwelveData:Keys:Fallback" "<TWELVE_DATA_FALLBACK_KEY>" --project backend/StockLab.Api/StockLab.Api.csproj
    dotnet user-secrets set "AlphaVantage:ApiKey" "<ALPHA_VANTAGE_KEY>" --project backend/StockLab.Api/StockLab.Api.csproj

La configuration versionnée définit `Jwt:Issuer`, `Jwt:Audience` et `Jwt:AccessTokenMinutes` (60 minutes). La valeur privée `Jwt:SigningKey` doit contenir au moins 32 octets UTF-8. La connexion SQL porte le nom `ConnectionStrings:StockLab`.

Avec des variables d’environnement, .NET utilise un double soulignement pour les clés imbriquées : `Jwt__SigningKey`, `ConnectionStrings__StockLab`, `TwelveData__Keys__Website`, `TwelveData__ActiveWebsiteKey`, `AlphaVantage__ApiKey` et `MarketData__Provider=TwelveData`. `Cors__AllowedOrigins__0` configure une origine frontend autorisée pour un accès direct à l’API. Le proxy Vite permet l’accès local via la même origine.

Pour les catalogues AI Trader, `AiTraderMetadata:RegistryPath`, `BacktestReportPaths`, `BenchmarkComparisonPaths` et `PromotionEvaluationPaths` désignent des exports privés existants accessibles au service backend. Leur publication atomique et leur provenance doivent être assurées par l’opérateur ; le frontend ne les fournit pas.

### Configuration ML

L’ingestion historique Python utilise `TWELVE_DATA_ML_API_KEY`, distincte des clés du backend .NET. Les traitements sur fichiers et les tests hors ligne n’en ont pas besoin.

Le stockage facultatif d’artefacts S3 utilise `STOCKLAB_ML_S3_BUCKET`, `STOCKLAB_ML_AWS_REGION` et `STOCKLAB_ML_S3_PREFIX`, avec les identifiants privés fournis par la chaîne de credentials boto3. Le bucket et son préfixe doivent être existants et approuvés ; le code ne crée ni bucket, ni clés, ni politiques IAM. Voir le [contrat des artefacts S3](ml/README.md#model-artifact-storage--aws-s3-76).

Le réentraînement publie de nouvelles versions `Archived`. La promotion compare les preuves d’évaluation et nécessite une application explicite ; un nouvel entraînement ne remplace pas automatiquement la version `Active` d’une famille.

### Configuration cloud

La persistance SQL et l’adaptateur S3 des modèles sont implémentés. Leur déploiement, leurs connexions et leurs droits d’accès restent propres à l’environnement. Le modèle de réentraînement dans `ml/aws/` prépare EventBridge Scheduler, ECS Fargate et un volume EFS partagé ; il n’atteste pas d’un déploiement AWS et son planning est désactivé par défaut. Les intégrations applicatives Azure Service Bus, AWS SQS et Lambda restent à développer. Aucun secret cloud ni chaîne de connexion ne doit être placé dans ce dépôt.

## Commandes utiles

Lancez ces commandes depuis la racine du dépôt :

    # Frontend
    npm --prefix frontend run lint
    npm --prefix frontend run build
    npm --prefix frontend test

    # Backend
    dotnet build backend/StockLab.sln
    dotnet test backend/StockLab.sln --filter "Category!=SqlServer"

    # ML
    python -m pytest ml

La commande ML suppose l’environnement Python préparé ci-dessus. La suite frontend couvre les contrats API, les hooks, les pages rendues et le routage HTTP. Les tests backend ordinaires utilisent notamment des providers simulés et SQLite isolé. Les tests ML utilisent des fixtures synthétiques et bloquent les appels réseau externes.

Sous Windows avec SQL Server LocalDB installé et démarré, lancez séparément les tests du moteur SQL réel :

```powershell
$env:STOCKLAB_TEST_LOCALDB = '1'
$env:STOCKLAB_PRICE_ALERTS_SQL_CONNECTION = ''
dotnet test backend/StockLab.sln --filter 'Category=SqlServer'
```

Ces tests portent le trait xUnit `[Trait("Category", "SqlServer")]` : leur sélection ne dépend pas du nom de la classe ou de la méthode. Ils utilisent des bases LocalDB isolées, pas la connexion Azure de l’application. Sans configuration SQL, un `dotnet test` global peut signaler ces cas comme ignorés : cela ne valide pas les scénarios de concurrence et de `rowversion`. Voir la [documentation des tests CI backend](backend/README.md#backend-test-ci).

## Intégration continue

Le [workflow GitHub Actions](.github/workflows/ci.yml) s’exécute sur les PR vers `develop`/`main` et sur déclenchement manuel :

| Job | Vérifications |
| --- | --- |
| Backend | Restauration, audit NuGet, compilation Release et tests hors SQL Server sur Ubuntu/.NET 10. |
| Backend SQL Server | LocalDB sur Windows, découverte des cas SQL et exécution avec `STOCKLAB_TEST_LOCALDB=1`. |
| Frontend | Node 24, `npm ci`, audit npm, lint, build et tests. |
| ML | Python 3.12, installation, audit des dépendances et tests hors ligne. |

Le job SQL compare le rapport TRX à la liste des tests découverts. Il échoue si un cas attendu est absent, ignoré ou en échec, ou si la découverte ne trouve aucun cas. Les rapports et la liste attendue sont conservés dans l’artefact `backend-sql-server-results`. Une CI verte garantit donc l’exécution des cas SQL découverts par ce workflow ; elle ne remplace pas une vérification de déploiement Azure ou des fournisseurs externes.

## Workflow de développement

1. Vérifiez l’issue, les PR et le code existant, puis partez du dernier `develop` : `git switch develop` et `git pull --ff-only origin develop`.
2. Créez la branche demandée dans l’issue, avec un préfixe comme `feature/`, `fix/`, `refactor/`, `docs/`, `test/` ou `chore/`.
3. Gardez un seul changement logique par commit. Utilisez Conventional Commits en anglais et en minuscules : `type(scope): description`, par exemple `feat(auth): connect profile to backend`. Types autorisés : `feat`, `fix`, `chore`, `refactor`, `test`, `docs`.
4. Lancez les commandes de lint, build et test concernées avant le push.
5. Ouvrez une pull request vers `develop`, liez l’issue avec `Closes #<number>`, décrivez les changements et la validation, puis ajoutez des captures pour les modifications visuelles. Utilisez le [modèle partagé](.github/pull_request_template.md).
6. Traitez les commentaires de revue, fusionnez la PR approuvée et mettez à jour votre branche develop locale avant de commencer une autre issue.

Ne poussez pas directement vers main ou develop. Gardez les changements liés à l’issue et n’ajoutez pas de refactorisation sans rapport dans une PR de fonctionnalité.

## Sécurité

- Ne versionnez pas de clés API, mots de passe, chaînes de connexion, jetons, données utilisateur, jeux de données générés ou modèles entraînés.
- Utilisez les user-secrets .NET, les variables d’environnement ou le gestionnaire de secrets de l’équipe pour les identifiants locaux.
- Gardez les données et artefacts générés de `ml/data/raw`, `ml/data/processed` et `ml/models` hors de Git. Ne chargez que des artefacts de modèles de provenance approuvée.
- Le fournisseur par défaut utilise des données simulées. Les fournisseurs externes ont des limites de débit et des budgets de requêtes ; utilisez-les volontairement.
- StockLab est un projet d’apprentissage en paper trading. Le code actuel n’envoie pas d’ordres réels à un courtier.
- Signalez les vulnérabilités présumées de manière privée aux responsables du projet au lieu d’ouvrir une issue publique contenant des secrets ou des détails d’exploitation.

## Limites et feuille de route

- Connecter l’interface Alerts à l’API (#57), persister le déclenchement des alertes (#43) et compléter les tests dédiés watchlist/alertes (#89).
- Déployer les commandes d’inférence (#257) et de risque/exécution simulée (#258), puis compléter la synchronisation des données et exports, la planification périodique et les captures de performance (#259). Un bot autonome opérationnel reste à déployer.
- Intégrer les événements de domaine/Azure Service Bus et les tâches AWS SQS/Lambda. Le stockage S3 des modèles est déjà disponible ; les autres usages cloud et leur exploitation doivent être traités séparément.
- Lever, si nécessaire, le plafond de vingt cotations de la page Portfolio. Au-delà, ses agrégats de marché restent indisponibles ; les positions et les montants comptables restent consultables.
- Implémenter les parcours OAuth Google et mot de passe oublié, actuellement sans service connecté.

Le mode `Mock` et les fixtures de tests sont des outils de développement. Ils ne prouvent pas la disponibilité de données réelles en production. Les champs sans API et les services indisponibles doivent conserver un état explicite ; aucune donnée de compte inventée ne doit les remplacer.

## Contribution

Lisez le README du composant concerné avant de modifier le frontend, le backend ou le ML. Créez ou mettez à jour une issue, gardez la branche ciblée, lancez les vérifications applicables et utilisez le modèle de pull request. Pour une modification d’interface, indiquez la route vérifiée, les dimensions d’écran testées et des captures lorsqu’elles aident la revue.

Documentation utile :

- [Frontend](frontend/README.md)
- [AI Trader : prérequis opérationnels](frontend/AI_TRADER_OPERATIONS.md)
- [Backend et contrats HTTP](backend/README.md)
- [Schéma relationnel](backend/database-schema.md)
- [ML : pipelines, modèles, backtests et artefacts](ml/README.md)

## Licence

Aucun fichier de licence n’est déclaré dans ce dépôt. Demandez l’accord des responsables du projet avant toute redistribution ou réutilisation.
