# Career Livery Manager — Relatório de Engenharia Reversa

Status original: **somente leitura**. A partir da Seção 14 (Fase 2), a investigação passou a incluir testes práticos autorizados explicitamente pelo usuário, com backup prévio em todos os casos, e restauração completa ao final de cada teste malsucedido. Nenhuma alteração foi deixada aplicada — o estado final de todos os arquivos do MSFS é o original.

Data: 2026-09-06 (atualizado no mesmo dia, sessão estendida — "Fase 2")
Instalação analisada: `D:\msfs24` (Steam, Official2024 + Community)

---

## 0. Resumo executivo

A descoberta mais importante desta investigação **não estava na lista original de hipóteses**: no MSFS 2024 (distribuição Steam/Marketplace), os pacotes `Official2024` **não contêm mais `livery.cfg`/`aircraft.cfg` em texto plano**. Eles são compilados dentro de arquivos binários proprietários (`*.fsarchive`, assinatura mágica `RASA`). Isso significa que:

- Não é possível ler nem editar o `livery.cfg` real do A321 oficial ou do Longitude oficial diretamente do disco.
- O mecanismo de seleção de livery só pode ser observado de forma legível em **pacotes Community** (add-ons, incluindo repaints "oficiais" redistribuídos por criadores da comunidade), porque o Community continua usando arquivos de configuração em texto plano.
- A comparação A321-vs-Longitude, portanto, teve que ser feita por **estrutura de pacote** (tipo de SimObject, presença/ausência de sistema modular de tags) em vez de diff direto de `livery.cfg`, complementada por exemplos reais de `livery.cfg` de liveries Community para A321/A320/A330/737MAX já instaladas.

Achado estrutural chave: o Longitude e o PC-24 (as duas aeronaves citadas no fórum como "não funciona") pertencem à categoria de pacote **`asobo-passiveaircraft-*`** (SimObject simplificado, originalmente pensado para tráfego AI/decoração, reaproveitado no Career como avião "gerido"). O A321 (que funciona) é um **`microsoft-aircraft-*`** completo, com sistema modular de partes intercambiáveis e tags. Essa diferença de categoria de pacote é a pista mais concreta encontrada para explicar por que o truque funciona num caso e não no outro.

---

## 1. Estrutura encontrada do A321

Pacote oficial: `D:\msfs24\Official2024\Steam\microsoft-aircraft-a321\`
`manifest.json`: `content_type = AIRCRAFT`, `title = "Airbus A321"`, criador reportado como parceiro (iniBuilds, via pastas `inibuilds\`).

```
microsoft-aircraft-a321/
├── ccrtelez.fsarchive          ← arquivo binário RASA (48 MB) — dados compilados
├── minimal.fsarchive
├── layout.json                 ← lista TODOS os arquivos "soltos" do pacote (texturas, sons, etc.)
├── manifest.json
├── contentinfo/
├── data/                       ← FMGS, ISIS, navdata, etc. (sistemas do avião)
├── html_ui/                    ← instrumentos glass cockpit
└── simobjects/airplanes/microsoft-a321/
    ├── attachments/inibuilds/  ← ~20 subpastas: part_exterior_airframe, part_exterior_engines_leap,
    │                              part_exterior_wings, part_interior_cabin_*, part_seat_bus, part_seat_eco...
    ├── presets/inibuilds/a21n/ ← preset de configuração (thumbnails de variação, ex.: sharklets)
    ├── liveries/inibuilds/     ← 10 pastas de livery: air new zealand, airbus house, easyjet,
    │                              global freight, jet2, klm, orbit, pacifica, spirit, white, wizz,
    │                              world travel
    └── common/                 ← model, sound, soundai
```

Cada pasta de livery (`liveries/inibuilds/wizz/`, etc.) contém apenas `texture/` e `thumbnail/` — **nenhum `livery.cfg` está presente no disco**. Confirmado via `layout.json` (518 KB, todo o índice de arquivos do pacote): não há nenhuma entrada `.cfg` em lugar nenhum do pacote. As texturas (`.ktx2`) aparecem normalmente no `layout.json`, o que prova que o pacote não é "tudo compactado" — apenas os arquivos de configuração/definição (livery.cfg, aircraft.cfg) foram removidos do layout solto e movidos para dentro do `.fsarchive`.

---

## 2. Estrutura encontrada do Longitude

Pacote oficial: `D:\msfs24\Official2024\Steam\asobo-passiveaircraft-citation-longitude\`
`manifest.json`: `content_type = AIRCRAFT`, `title = "Citation Longitude"`.

Ponto crítico: **não existe** um pacote separado tipo `asobo-aircraft-longitude` (aeronave "completa"/voável com sistemas complexos). O único pacote instalado para o Longitude é o `passiveaircraft`. Isso é o mesmo padrão usado para o CJ4: existe `asobo-aircraft-cj4` (voável, completo) **e** `asobo-passiveaircraft-citation-cj4` (versão simplificada usada como tráfego AI). Para o Longitude e o PC-24, **só existe a versão "passive"** — ou seja, o avião que você pilota no Career é, estruturalmente, o mesmo SimObject simplificado usado para tráfego aéreo gerado por IA.

```
asobo-passiveaircraft-citation-longitude/
├── simobjects/airplanes/asobo_passiveaircraft_cit_longitude/
│   ├── attachments/asobo/
│   │   ├── function_exterior/
│   │   └── part_exterior/       ← UMA única parte externa monolítica (sem variantes de motor/asa)
│   ├── common/model/
│   └── liveries/asobo/
│       └── citation_longitude_official/   ← única livery presente, contém só texture/
└── (sem pasta "presets/")
```

Diferenças estruturais imediatas frente ao A321:
- **Sem pasta `presets/`** (o A321 tem `presets/inibuilds/a21n/`).
- **Apenas uma `part_exterior`** monolítica, contra ~10 partes modulares intercambiáveis do A321 (fuselagem, motores LEAP, asas, várias seções de cabine, bancos).
- **Apenas uma livery oficial** instalada (`citation_longitude_official`) contra 10 liveries do A321.

O PC-24 (`asobo-passiveaircraft-pc24`) foi verificado como controle e apresenta **exatamente o mesmo padrão**: `attachments/asobo/{function_exterior, part_exterior}` únicos, sem `presets/`, uma única livery (`pc24_official`). Isso reforça que a característica relevante não é "ser o Longitude" especificamente, mas sim **pertencer à família de pacotes `passiveaircraft`**.

---

## 3. Arquivos relevantes de cada um

| Arquivo/mecanismo | A321 (oficial) | Longitude (oficial) |
|---|---|---|
| `livery.cfg` (texto) | Ausente no disco (dentro do `.fsarchive`) | Ausente no disco (dentro do `.fsarchive`) |
| `aircraft.cfg` (texto) | Ausente | Ausente |
| `liveries.lbl` | Não encontrado em nenhum pacote instalado | Não encontrado |
| `manifest.json` | Presente, padrão | Presente, padrão |
| `layout.json` | Presente, 518 KB, lista texturas/sons mas nenhum `.cfg` | Presente, lista análoga |
| `presets/` | Presente (`inibuilds/a21n`) | **Ausente** |
| Tags de variante (`part_exterior_engines_leap`, `part_exterior_wings`, etc.) | Presente — sistema modular multi-peça | **Ausente** — peça única |
| `.fsarchive` (RASA binário) | `ccrtelez.fsarchive` (48 MB) + `minimal.fsarchive` | (mesmo padrão, arquivo próprio) |

Nenhum arquivo específico do Career (`career.cfg`, `dressing.cfg`, etc.) foi encontrado em nenhum pacote instalado, oficial ou Community. Isso sugere fortemente que a lógica de seleção de livery do Career está **compilada no executável do simulador**, não exposta como configuração de dados — o que é coerente com o fato de a comunidade só ter descoberto o comportamento por tentativa e erro, sem nenhuma documentação oficial sobre isso.

---

## 4. Como as liveries são realmente registradas (evidência de pacotes Community)

Como o conteúdo oficial está compilado, a única forma de ver o schema real de `livery.cfg` em uso é através de pacotes Community instalados nesta máquina. Foram encontrados 6 arquivos `livery.cfg` em texto puro:

```
Community/asobo_b737max-GLO-PRXMT/.../Gol PR-XMT/livery.cfg
Community/Azul Linhas Aéreas A330-200.../A330-200_AZU_PRAIW_RR/livery.cfg
Community/microsoft-a320neo-azul-PR-YSO/.../A320neo_Azul-PR-YSO/livery.cfg
Community/microsoft-aircraft-a320neo-AZU-PRYSG/.../Azul PR-YSG/livery.cfg
Community/microsoft-aircraft-a320neo-TAM-PRXBP-b/.../livery.cfg
Community/microsoft-aircraft-a321-LAN-PS-LBA/.../A321_LAN_PS-LBA/livery.cfg   ← A321, mesmo aircraft-base do teste do fórum
```

Exemplo típico (A321, Latam PS-LBA):

```ini
[Version]
major = 1
minor = 0

[General]
name="Latam PS-LBA"

[Selection]
required_tags = "a20n_eng_leap"
required_tags = "wings"

[Panel_DynamicParameters]
param.0="exterior_registration_number_color,black"
```

Observações sobre o schema real (MSFS 2024 modular), diferente do `aircraft.cfg` clássico do FS2020:

- **Não existe seção `[FLTSIM.N]`** em nenhum dos 6 arquivos. O sistema `[FLTSIM.N]` clássico (com `title=`, `model=`, `texture=`, `ui_variation=`) só apareceu no addon **FlyByWire A320** (`SimObjects/AirPlanes/FlyByWire_A320_NEO/aircraft.cfg`), que ainda usa o formato legado FS2020 (não é modular). Isso confirma que aeronaves modulares nativas do MSFS 2024 abandonaram o `[FLTSIM.N]` como mecanismo de registro de livery.
- `[Selection] required_tags` é o mecanismo real de "encaixe": ele diz a quais **peças modulares (`attachments/`) com aquela tag** essa livery se aplica (ex.: motor LEAP vs. motor PW, com ou sem winglets/"wings"). Isso é sobre **compatibilidade de modelo 3D**, não sobre Career.
- `[General] name` é apenas o rótulo mostrado no Configurador — confirmado, não achamos nenhuma outra seção que sirva de "nome" alternativo.
- **`[Specialization] dressing_codes` e `[Tags] tag.N` só aparecem em UM dos 6 exemplos**: a livery Gol PR-XMT do 737 MAX, criada por "jffs":

```ini
[Version]
major = 1
minor = 0

[Selection]
required_tags = "passenger"

[Specialization]
dressing_codes = "COF-PCC, PRC-PSO"

[Tags]
tag.0 = "Licence_Airline"

[GENERAL]
Name=Gol Linhas Aéreas PR-XMT
atc_id="PR-XMT"
atc_parking_codes="GLO"
icao_airline="GLO"
atc_airline="Gol"
sound=""
```

Este é o achado mais valioso da investigação: é a **única evidência real, em disco, de alguém aplicando deliberadamente os mecanismos documentados (`dressing_codes`, `tag.0`) num `livery.cfg`** — provavelmente porque o criador dessa livery já havia investigado (ou soube) que esses campos afetam a elegibilidade da livery no Career/atividades. Os códigos `COF-PCC` / `PRC-PSO` não estão documentados publicamente; pelo padrão de nomenclatura (prefixos de 3 letras + sufixo), parecem ser identificadores internos de "dressing code" (código de especialização) por tipo de atividade — hipótese, não confirmado.

**Nenhuma das liveries A321/A320/A330 testadas usa `[Specialization]` ou `[Tags]`.** Elas dependem apenas de `[Selection] required_tags` (compatibilidade de peça 3D) e do `[General] name`.

---

## 5. Teste da hipótese de ordenação alfabética

### Fatos coletados

1. Todas as liveries oficiais e Community observadas (que têm `[General] name`) **não têm `[Specialization]`/`[Tags]`** — ou seja, segundo a documentação oficial ("dressing_codes vazio = utilizável em todas as carreiras"), a maioria das liveries é, por padrão, elegível para qualquer especialização de Career.
2. Isso é consistente com o relato do fórum: se a maior parte das liveries de um avião já é elegível por padrão, resta ao Career escolher **uma entre várias candidatas igualmente elegíveis** — e é exatamente aí que a ordenação (por nome, por pasta, ou por ordem de enumeração do pacote) se torna o fator decisivo.
3. A pasta oficial do A321 tem **10 liveries candidatas** competindo (todas plausivelmente elegíveis por padrão). A pasta oficial do Longitude tem **apenas 1** livery oficial instalada. Um repaint customizado adicionado via Community se torna a **2ª candidata**.
4. O A321 tem sistema de tags modular rico (`required_tags` cruzando múltiplas peças); o Longitude não tem esse sistema (peça única).

### Hipóteses (não decididas — faltam testes empíricos)

- **H1 — Ordenação alfabética pura por `[General] name`, aplicada igualmente a todos os aviões.**
  Evidência a favor: bate com o relato original do fórum para o A321.
  Evidência contra: não explica por que falha no Longitude/PC-24, já que ambos também têm `[General] name` disponível para edição.

- **H2 — Ordenação alfabética ocorre, mas só age sobre o subconjunto de liveries que já passou pelo filtro de `[Selection] required_tags`/tags de Career.**
  Se o Longitude (peça única, sem sistema de tags) trata sua **única livery oficial de forma especial/fixa** (por exemplo, referenciada por nome de pasta ou ID interno específico em vez de "primeira da lista enumerada"), a reordenação alfabética simplesmente não teria efeito, pois o Career nunca "enumera" — ele aponta direto para aquele contentor.

- **H3 — Aeronaves da família `passiveaircraft` usam um caminho de código diferente no Career** (por serem originalmente pensadas para tráfego AI, não para seleção de livery pelo jogador), e esse caminho **não itera dinamicamente as liveries instaladas**, ao contrário do caminho usado por aeronaves "completas" como o A321.
  Evidência a favor: correlação estrutural direta — as duas aeronaves onde o truque falhou (Longitude, PC-24) são as duas únicas testadas que pertencem à família `passiveaircraft`; a aeronave onde funcionou (A321) é uma aeronave completa.

- **H4 — O problema relatado para Longitude/PC-24 não é sobre o algoritmo de seleção, e sim sobre o pacote de livery customizado não estar corretamente formatado/registrado** (ex.: faltando `required_tags` compatível, arquivo em pasta errada, cache do sim não atualizado). Não podemos descartar erro de usuário nos relatos do fórum, já que não são reprodução controlada.

### Experimento que distinguiria as hipóteses

Sem alterar nada agora, mas para quando formos testar (com autorização futura):

1. Pegar uma livery Community **já funcional e visível** no Configurador de uma aeronave `passiveaircraft` (ex.: se existir alguma livery Community para CJ4, PC-24 ou Longitude) e renomear apenas o `[General] name` para algo que ordene alfabeticamente antes do nome da livery oficial (`citation_longitude_official`/`pc24_official`).
2. Entrar no Career com aquele avião e observar se a livery aplicada muda.
   - Se mudar → H1/H2 corretas (ordenação por nome funciona mesmo no Longitude) → o relato do fórum para Longitude tinha outro problema (H4).
   - Se **não** mudar → suporta H3 (mecanismo de seleção diferente para `passiveaircraft`).
3. Em paralelo, testar no A321: adicionar `[Specialization] dressing_codes=""` e `[Tags] tag.0="Licence_Airline"` (copiando o padrão usado na livery Gol PR-XMT) numa livery Community do A321 **sem** renomear o `name`, e ver se isso sozinho já influencia a ordem/seleção do Career, independente do nome. Isso separaria "efeito do nome" de "efeito das tags/dressing_codes".
4. Comparar o comportamento entre CJ4 (`asobo-aircraft-cj4`, aeronave completa) e `asobo-passiveaircraft-citation-cj4` (mesma aeronave, mas pacote "passive") — se o Career para missões de charter usa o CJ4 "completo" (não o passive), isso ajudaria a confirmar se o Longitude/PC-24 realmente rodam sobre o SimObject "passive" durante o Career, ou se existe algum outro pacote de Longitude "completo" que não está instalado nesta máquina (recomendo confirmar isso dentro do próprio jogo, verificando qual pasta de simobject é carregada quando o avião entra em cena — por exemplo via `Process Monitor`/`ProcMon`, só leitura, sem modificar nada).

---

## 6. Por que o hack "AA Wizz" provavelmente funciona no A321

Combinação de fatores estruturais observados:
- É uma aeronave "completa" (`microsoft-aircraft-a321`), não uma `passiveaircraft`.
- Tem 10 liveries oficiais concorrendo, todas presumivelmente sem `dressing_codes` restritivo (elegíveis por padrão para todas as carreiras) — logo o desempate por algum critério de ordenação (nome, pasta, ou ordem de enumeração) decide qual é escolhida.
- Tem sistema de tags modular (`required_tags`) já demonstrado em uso normal por criadores de conteúdo para A321/A320/A330, o que mostra que esse avião expõe e respeita corretamente customizações de livery vindas do Community.

## 7. Por que provavelmente não funciona no Longitude/PC-24

- Ambos pertencem à família `asobo-passiveaircraft-*`, com um SimObject muito mais simples (uma única peça externa, sem sistema de tags modulares, sem pasta `presets/`).
- Isso sugere (hipótese H3) que o motor de Career pode referenciar a livery "por contentor fixo" em vez de "primeira da lista enumerada" para essas aeronaves mais simples — quebrando a premissa do truque de ordenação alfabética.
- Alternativamente (H4), pode ser apenas que, como há muito menos exemplos de liveries Community para essas aeronaves no ecossistema (não encontramos nenhuma instalada aqui), os usuários que tentaram o truque podem ter cometido erros de empacotamento não relacionados ao mecanismo do Career em si.

---

## 8. Testes manuais recomendados (para quando houver autorização de modificar arquivos)

1. Fazer backup completo da pasta Community e do save de Career antes de qualquer teste.
2. Testar a hipótese de ordenação isolando UMA variável de cada vez (nome vs. tags vs. dressing_codes), conforme o experimento da seção 5.
3. Usar `Process Monitor` (Sysinternals, somente leitura) durante a troca de avião no Career para ver quais arquivos o `FlightSimulator2024.exe` abre relacionados a `livery`/`dressing`/`career` — isso pode revelar se existe algum arquivo de save/estado que registra qual livery foi escolhida para aquele avião do Career (provavelmente dentro do save do Career, não dentro do pacote da aeronave).
4. Inspecionar o **save do Career** do usuário (fora de `Official2024`/`Community`, normalmente em `%APPDATA%\...\Packages\...\SystemAppData\wcgs\` ou pasta de saves do MSFS) à procura de alguma referência a nome de livery/contentor associada ao avião possuído — isso pode ser o verdadeiro "banco de dados" de qual livery está atribuída a cada avião do Career, e não algo decidido dinamicamente a cada carregamento. (Não explorado nesta rodada porque o pedido do usuário focou nos pacotes de aeronave; recomendo isso como próximo passo de investigação.)

---

## 9. Estratégia mais robusta para o Career Livery Manager

Dado que:
- Pacotes `Official2024` são compilados (`.fsarchive`, formato `RASA` proprietário) e não devem/podem ser editados com segurança;
- Pacotes `Community` permanecem em texto plano e são o mecanismo suportado de customização;
- O Career parece decidir a livery por algum critério de enumeração/ordenação ainda não 100% confirmado, possivelmente influenciável por `[General] name`, e possivelmente também por `[Specialization]`/`[Tags]`;

A estratégia recomendada é:

1. **Nunca tocar em `Official2024`.** O programa deve tratar esses pacotes como somente leitura (são inclusive binários incompreensíveis sem engenharia reversa do formato RASA, o que teria implicações de ToS/DRM).
2. Trabalhar exclusivamente criando/editando um **pacote Community dedicado** por avião gerenciado (ex.: `CareerLiveryManager-<simobject>-override`), contendo apenas os `livery.cfg` necessários (ou cópias leves apontando para as liveries já instaladas) com o `[General] name` ajustado para forçar a ordem desejada, e, quando aplicável, replicando o padrão `[Specialization] dressing_codes=""` + `[Tags] tag.0="Licence_Airline"` visto na livery Gol PR-XMT, para maximizar a chance de elegibilidade no Career.
3. Como o mecanismo de ordenação real ainda não está 100% confirmado (Seção 5), o programa deveria:
   - Detectar dinamicamente o critério que "vence" fazendo *probing* controlado (o experimento da seção 5), documentando o resultado por versão do simulador (o mecanismo pode mudar em patches).
   - Ter um modo "renomear para ordenar primeiro" (replica o truque do fórum) como fallback simples.
   - Ter um modo "editar tags/dressing_codes" como tentativa mais "correta" para aeronaves onde o truque de nome falhar.
4. Sempre fazer backup do pacote Community antes de qualquer escrita, e nunca escrever fora da pasta Community.
5. Nunca tocar no save do Career (dinheiro, XP, reputação, progressão) — o programa deve limitar-se estritamente a arquivos de definição de livery.

## 10. Arquivos que o programa precisaria eventualmente modificar

- `livery.cfg` dentro de pacotes **Community** (`[General] name`, opcionalmente `[Specialization]`/`[Tags]`).
- Possivelmente criar um pacote Community novo (com seu próprio `manifest.json` + `layout.json`) que sobrepõe/adiciona liveries com prioridade de carregamento adequada, sem duplicar texturas (referenciando os arquivos existentes, se o formato permitir, ou copiando-os).

## 11. Arquivos que NÃO devemos modificar

- Qualquer coisa dentro de `Official2024\Steam\*` — incluindo `*.fsarchive`, `manifest.json`, `layout.json` desses pacotes.
- Saves de Career (progressão, dinheiro, reputação, XP) — fora do escopo deste programa por definição do usuário.
- `Content.xml` do usuário só deve ser lido, nunca escrito automaticamente sem confirmação explícita (ele controla quais pacotes Community estão ativos).

## 12. Riscos de quebrar o MSFS/Marketplace/updates

- Editar pacotes `Official2024` é, na prática, inviável sem decodificar o formato `RASA` — e mesmo que fosse possível, atualizações do sim/Marketplace substituiriam o pacote inteiro, descartando qualquer edição e potencialmente falhando a verificação de integridade do pacote (o que pode ser tratado como violação dos termos do Marketplace).
- Mesmo edições em Community podem ser sobrescritas por atualizações do próprio addon (se o Community package em si for atualizado por um creator/marketplace) — o programa deveria preferir criar um pacote **separado e próprio** (nome de pasta claramente identificável, ex. prefixo `careerliverymanager-`) em vez de editar pacotes de terceiros in-place, para não conflitar com atualizações desses addons.
- Qualquer erro de sintaxe no `livery.cfg` gerado pode fazer o simulador não carregar aquele SimObject, ou (pior) causar erro de carregamento do avião inteiro — validação de schema antes de escrever é essencial.

## 13. Backup / Restore

- Antes de qualquer escrita: copiar a pasta inteira do pacote Community afetado (ou pelo menos o `livery.cfg` + `manifest.json`) para uma pasta de backup versionada por timestamp, dentro da própria pasta de dados do programa (fora de `Community`/`Official2024`).
- "Restore Original" = restaurar o backup mais recente daquele pacote específico.
- Se o programa criar seu próprio pacote Community (recomendado, ver Seção 9), o "restore" mais simples e seguro é apenas remover a pasta desse pacote (nunca tocando no addon de terceiros original).

---

## 14. Fase 2 — Testes práticos e descobertas (sessão estendida, mesmo dia)

Depois da Fase 1 (somente leitura), o usuário pediu para testar ativamente hipóteses na sua própria instalação, com autorização explícita a cada mudança e backup prévio. Esta seção documenta o que foi testado, o que se aprendeu, e o estado final (tudo restaurado).

### 14.1 O Longitude do usuário é o pacote completo, não o "passive"

Usando o **Process Monitor** (Sysinternals, captura de acessos a arquivo em tempo real) durante uma sessão real de Career, confirmamos que o jogo carrega `asobo_longitude` (aeronave completa), nunca `asobo_passiveaircraft_cit_longitude`. Isso **invalida a hipótese H3** da Fase 1 (que a família "passive" explicaria a diferença de comportamento frente ao A321) — essa hipótese foi construída sobre dados incompletos (não sabíamos, na Fase 1, que existia um sistema de "StreamedPackages" com o Longitude completo).

### 14.2 Descoberta do `StreamedPackages` e do `minimalcache`

Além de `Official2024\Steam` e `Community`, existem **duas outras árvores de conteúdo**:
- `D:\msfs24\StreamedPackages\` — 1098 pacotes, sistema de streaming sob demanda. Contém `fs24-asobo-aircraft-longitude` (completo) e 7 pacotes de livery legados do FS2020 (`fs20-asobo-aircraft-longitude-livery-{01,aviators,global,kenmore,orbit,pacifica,xbox-aviators}`).
- `D:\SteamLibrary\steamapps\common\MSFS2024\minimalcache\` — cache "sempre disponível" dentro da própria instalação do executável, com os mesmos 7 pacotes `fs20-...-livery-*` (mas registrados como stub — os `.fsarchive` que o `layout.json` lista **não existem fisicamente** nessa pasta, achatados/redirecionados por algum outro nível do sistema de arquivos virtual).

Baixar o Longitude pela loja do próprio jogo (função "Gerenciar na Minha Biblioteca") materializou um **terceiro** local: `Official2024\Steam\asobo-aircraft-longitude\`, um pacote modular completo (igual ao A321: `attachments/`, `presets/` não presente, mas `liveries/asobo/{01,default,global,kenmore,orbit,pacifica}` com estrutura idêntica entre si).

### 14.3 Testes de sobreposição via Community (todos sem sucesso no render 3D)

Testamos, nessa ordem, pacotes Community que sobrepõem `simobjects/airplanes/asobo_longitude/liveries/asobo/01/...` com o conteúdo da livery `default` (branca):

1. Cópia completa (modelo + textura) com `layout.json` gerado à mão (só `path`/`size`/`date`) → **thumbnail mudou pra branco, modelo 3D continuou vermelho**.
2. Mesma cópia, mas com `layout.json` incluindo `uncompressed_size` correto (copiado do `layout.json` oficial) para os arquivos `.fsc` compilados → **mesmo resultado**.
3. Simplificação: pacote só com a pasta de textura (sem modelo, já que a geometria é idêntica entre liveries) → **mesmo resultado**.
4. Teste de hipótese alternativa: sobrepor o SimObject **legado** `asobo_longitude_livery_01` (separado, formato antigo, `model.01`/`texture.01`) com o conteúdo completo da livery legada `aviators` → não testado até o fim (partiu-se para o Plano B antes de concluir).

**Conclusão desta seção**: pacotes Community, mesmo tecnicamente corretos (`layout.json` validado, caminhos batendo em maiúsculas/minúsculas com o oficial), **não conseguiram alterar o render 3D em nenhum teste** — só afetaram artefatos simples (thumbnail) que aparentemente são lidos por um caminho de código diferente (mais direto/menos validado) do que o modelo/textura real usados em voo.

### 14.4 Teste direto no pacote oficial (Plano B) — parcialmente revelador, mas instável

Com autorização explícita do usuário, e backup completo prévio (`D:\msfs24\_quarentena_mods\backup-asobo-aircraft-longitude\`), foi feita uma edição direta em `Official2024\Steam\asobo-aircraft-longitude`:
- A pasta `liveries/asobo/01` (vermelha) foi renomeada para `02`.
- Uma cópia de `liveries/asobo/default` (branca) foi colocada em `liveries/asobo/01`.
- O `layout.json` do pacote foi reescrito para refletir a mudança, usando os campos originais (`size`, `hash`, `uncompressed_size`, `date`) de cada arquivo.
- O `ROLLINGCACHE.CCC` foi resetado (renomeado) antes e depois do teste, para eliminar qualquer cache antigo.

**Resultado**: instável. A nova "01" (fisicamente = conteúdo da `default`) continuou renderizando **vermelha** em jogo. A "02" (fisicamente = conteúdo original da "01") passou a renderizar **completamente branca, sem nenhuma textura** (pior que antes — nem a pintura vermelha nem a branca corretas). No Career, a livery ficou **piscando** entre vermelho e branco, com o vermelho aparentemente em baixa resolução (sugerindo um LOD/fallback).

Um trace do Process Monitor feito durante esse teste mostrou que o jogo **não abriu nenhum arquivo** sob `liveries/asobo/01` ou `liveries/asobo/02` durante a sessão — só leu bytes do `ROLLINGCACHE.CCC` por offset interno. Isso indica que, para esse SimObject específico, o motor resolve o conteúdo por algum **identificador interno cacheado** (não pelo caminho do arquivo em si), o que explica por que trocar arquivos fisicamente no disco não teve o efeito esperado de forma limpa.

**Tudo foi restaurado** ao estado original ao final deste teste (pastas, `layout.json`, cache resetado de novo).

### 14.5 O verdadeiro mecanismo: `AircraftProfiles.json` e IDs de variação opacos

Localizamos, na pasta de instalação do jogo (`D:\SteamLibrary\steamapps\common\MSFS2024\Packages\fs-career-asobo\GameModeData\Asobo\Career\`), o pacote de dados do **modo Career em si** (`fs-career-asobo`) — separado de qualquer pacote de aeronave. Dentro dele:

- **`AircraftProfiles.json`** (texto puro, editável): lista 28 `AircraftProfileId`/`ActivityDressingId` (exatamente os códigos vistos em `dressing_codes`, ex.: `COFPCC`, `PRCPSO`), cada um com uma lista fixa de **`VariationIds`** — números inteiros de 64 bits (ex.: `17640125035082564079`) que identificam variações/liveries específicas autorizadas para aquela categoria de missão.
- Testamos se esses números são um hash simples do nome/título da livery (FNV-1a 64 bits sobre várias strings candidatas) — **nenhum bateu**. Isso indica que são **GUIDs/IDs atribuídos pela ferramenta de build oficial da Asobo** no momento da compilação do pacote, não algo derivável do nome ou da estrutura de pastas.
- **`Liveries_Patterns.spb`**: decodificado com sucesso (ver 14.6) — não contém esses números; é um sistema separado (nomes de "Licence" por categoria de carreira, ex.: `Licence_PrivateCharter`, `Licence_Airline`), usado nos `[Tags] tag.0=` do `livery.cfg`.

**Correção importante**: a livery de teste do 737 MAX (Gol PR-XMT) que usamos como referência na Fase 1 usa `tag.0 = "Licence_Airline"` — mas o Longitude é uma aeronave de **Private Charter** (confirmado pelo texto oficial "Can be used in the following Microsoft Flight Simulator 2024 missions: VIP Charter"), então a tag correta a testar seria **`Licence_PrivateCharter`**, não `Licence_Airline`. Isso não foi testado até o fim da sessão (ficou como próximo passo).

**Conclusão**: o mecanismo real de elegibilidade de livery no Career é uma lista **explícita e fixa** de IDs de variação por categoria de missão, mantida num arquivo JSON oficial (editável, mas cujos valores não temos como gerar para conteúdo customizado sem a ferramenta de build oficial da Asobo/SDK do MSFS). Isso explica de forma unificada **todos** os resultados negativos da Fase 1 e da Fase 2: nome, tag, ordem de pasta, cópia de conteúdo — nada disso muda o ID de variação interno de uma livery.

### 14.6 Ferramenta `spb2xml` — decodificação de `.spb` (sucesso)

Diferente do `.fsarchive` (formato `RASA`, protegido por DRM, sem ferramenta pública conhecida — confirmado por pesquisa: a própria comunidade cobra a Asobo há anos por isso, ver tópicos "Asobo, unlock the .fsarchive..." e "[VFS] We need to find a way to stop this madness" em devsupport.flightsimulator.com), o formato **`.spb`** ("SimProp Binary", usado em missões e dados de Career) **tem ferramenta de conversão pública**:

- Ferramenta: [`spb2xml`](https://github.com/leppie/spb2xml) (release `spb2xml-msfs-1.0.1.zip`, ~92 KB), mantida pela comunidade, anunciada oficialmente no fórum de devs da Asobo.
- Uso: `spb2xml-msfs.exe -s "<caminho>\MSFS2024\Propdefs\1.0\Common" arquivo.spb` (a flag `-s` só é necessária na primeira execução; gera um `propdefs.cache`). Sem argumento de arquivo, roda recursivamente em todos os `.spb` do diretório atual.
- Baixada e testada nesta sessão (com autorização do usuário) em `Liveries_Patterns.spb`, `Career.spb`, e recursivamente em toda a árvore `GameModeData\Asobo\Career\` (parou por um erro de caminho longo do Windows, mas decodificou centenas de arquivos antes disso).
- **Resultado**: confirma a existência de um sistema de "Licence" por especialização de carreira (`Licence_Tour`, `Licence_Airline`, `Licence_PrivateCharter`, `Licence_Firefighting`, etc., cada um com um GUID próprio) — mas esse sistema controla o **tema/roteiro da missão** (ex.: presença de comissário de bordo, diálogos, fluxo de voo), não a seleção de qual livery é usada visualmente. Não encontramos, nos arquivos decodificados até o momento, uma referência direta cruzando os números `VariationIds` do `AircraftProfiles.json` com nomes de aeronave/livery legíveis.

### 14.7 Pesquisa externa — confirmação de bug conhecido, não solucionado

Pesquisa nos fóruns oficiais (setembro de 2026) encontrou:
- [Citation Longitude Career mode bug](https://devsupport.flightsimulator.com/t/citation-longitude-career-mode-bug/14182) — bug reportado oficialmente.
- [Citation Longitude Liveries not loading in Career mode](https://forums.flightsimulator.com/t/citation-longitude-liveries-not-loading-in-career-mode/740322) — um usuário relata que **instalar uma livery de terceiro** (pacote comprado da 4SIMMERS) deixou o avião **completamente branco no Career** (o mesmo sintoma observado no nosso teste da Seção 14.4 com a pasta "02"!); a solução dele foi desinstalar o pacote de terceiro e resetar a carreira. Outro usuário no mesmo tópico declara: *"até onde eu sei, não dá pra trocar a livery no Career mode em nenhum avião"*.
- [Longitude C700 - Missing liveries](https://forums.flightsimulator.com/t/longitude-c700-missing-liveries/725987) — confirma que mesmo as liveries **oficiais** anunciadas (global, kenmore, orbit, pacifica) não aparecem de forma confiável pra muitos jogadores; um criador de livery experiente relata que "é muito complicado fazer liveries pro 2024", e que "os poucos que fizeram liveries do CJ4 pra 2024 disseram que foi uma dor" — sugerindo que a limitação atinge outras aeronaves "premium"/"deluxe" além do Longitude, não é exclusiva dele.

**Isso reclassifica o problema**: não é (só) uma limitação de mecanismo que a gente precisa descobrir — é um **bug/limitação reconhecido e não resolvido da Asobo**, afetando a arte 3D modular do 2024 de forma mais ampla.

## 15. Conclusão final revisada

1. O Career trava a livery do Longitude num **ID de variação interno específico**, listado em `AircraftProfiles.json` (`fs-career-asobo`), não resolvido dinamicamente a partir de nome/tag/ordem de pasta/conteúdo de arquivo.
2. Esse ID é, aparentemente, atribuído pela ferramenta de build oficial da Asobo — não é um hash simples derivável do nome, e não temos como gerá-lo para conteúdo customizado sem o SDK oficial do MSFS.
3. Isso é agravado por um **bug reconhecido pela comunidade** (não exclusivo do Longitude) no pipeline de arte modular do MSFS 2024, onde até liveries oficiais falham em aparecer corretamente.
4. **Instalar liveries de terceiros pode piorar a situação** (render totalmente branco/quebrado), replicando exatamente o que outro usuário relatou de forma independente.
5. O único caminho tecnicamente correto pra fazer uma livery customizada aparecer no Career seria: compilar com o **SDK oficial do MSFS** (que atribui um ID de variação de verdade) e então adicionar esse ID ao `VariationIds` do perfil correto em `AircraftProfiles.json`. Isso é um projeto de desenvolvimento de conteúdo, não uma edição de configuração.
6. Para o "Career Livery Manager": recomenda-se **não** oferecer suporte ao Longitude (nem a aeronaves na mesma situação, ex. CJ4) até a Asobo corrigir isso — o programa deveria detectar esses casos e avisar "não suportado (bug conhecido do jogo)" em vez de tentar aplicar qualquer truque, para não arriscar deixar o avião do usuário com renderização quebrada (branco/sem textura) como vimos acontecer.
7. O A321 continua sendo o único caso com evidência real (ainda que frágil, um único relato) de que a técnica de renomear `[General] name` funciona — mas nem esse caso foi retestado nesta sessão.

## 16. Ferramentas agora disponíveis para investigação futura

- **Process Monitor** (Sysinternals) — já usado, útil para rastrear acesso a arquivo em tempo real.
- **`spb2xml`** (`C:\Users\Antonio\AppData\Local\Temp\claude\...\scratchpad\spb2xml\extracted\spb2xml-msfs.exe` nesta sessão — recomenda-se mover para um local permanente se for continuar usando) — decodifica `.spb` para XML.
- **`.fsarchive` continua opaco** — sem ferramenta pública conhecida; a própria comunidade não conseguiu decifrar esse formato até a data desta pesquisa.

## 17. A RECEITA VALIDADA — funciona de verdade (confirmado em 2 de 2 testes)

Depois de tudo documentado nas seções 14–16, uma combinação específica **funcionou** para fazer o Career carregar uma livery customizada no avião próprio — confirmada em dois aviões diferentes (Cessna Citation Longitude com a livery NetJets N823QS, e Cirrus SF50 com a livery N93SJ). Isso contradiz a citação oficial da Asobo (Seção 15) de que a troca de livery "não existe ainda" — na prática, existe uma forma de contornar isso do lado do jogador, mesmo que a Asobo não ofereça isso pela UI.

### 17.1 Em que consiste a receita

A combinação que funciona tem **dois ingredientes**, e nenhum dos dois sozinho basta (testamos isso extensivamente nas seções anteriores):

1. **Registrar a livery como se fosse do fabricante oficial** — ou seja, colocar a pasta da livery dentro de `simobjects/airplanes/<simobject>/liveries/<VENDOR_OFICIAL>/`, usando o mesmo nome de pasta "vendor" que a Asobo usa pra aquele avião (`asobo`, `microsoft`, etc. — descoberto olhando a estrutura do pacote oficial em `Official2024\Steam\<pacote>\...\liveries\`), **em vez de** usar o nome do criador da livery de terceiro (ex.: `TimHH`, `ryanbatc`). Isso é feito via um pacote **Community novo**, sem tocar em nenhum arquivo oficial.
2. **Prefixar o nome com `!`** — tanto o nome da pasta da livery quanto (o mais importante) o valor de `[GENERAL] Name=` dentro do `livery.cfg`. `!` vem antes de números e letras na ordenação ASCII.

**Pré-requisito**: a aeronave precisa usar o sistema moderno de `livery.cfg` (a maioria das aeronaves "premium"/"deluxe" do MSFS 2024 usa). **Não funciona** em aeronaves com liveries no formato antigo, só-textura, sem `livery.cfg` (testamos no Honda HA-420 HondaJet e não funcionou — lá só existe o nome da pasta, sem campo de nome pra prefixar dentro de um arquivo).

### 17.2 Passo a passo (procedimento usado nos dois casos de sucesso)

1. Identifique o SimObject e a pasta "vendor" oficial da aeronave: abra `Official2024\Steam\<pacote-da-aeronave>\...\simobjects\airplanes\<simobject>\liveries\` e veja o nome da(s) pasta(s) ali dentro (ex.: `asobo`, `microsoft`).
2. Pegue a livery de terceiro que você quer usar (formato `SimObjects\Airplanes\<simobject>\liveries\<criador>\<nome_da_livery>\`, contendo `livery.cfg` + texturas, e opcionalmente uma variante `_DR` de "Dynamic Registration").
3. Crie um pacote **Community novo e vazio** (pasta própria, nunca reaproveitar/editar o pacote original de terceiro nem o oficial).
4. Copie a(s) pasta(s) da livery para dentro de `simobjects/airplanes/<simobject>/liveries/<VENDOR_OFICIAL>/`, renomeando a pasta de destino para começar com `!` (ex.: `!NomeDaLivery`, `!NomeDaLivery_DR`).
5. Edite o `livery.cfg` de cada variante: `[GENERAL] Name="!..."` (prefixando o nome exibido também).
6. Se existir uma variante `_DR`, o `texture\texture.cfg` dela tem uma linha `fallback.1=..\..\<NOME_ANTIGO_DA_PASTA>\texture` — atualize para o novo nome de pasta (com `!`). Os outros arquivos (`livery.xml`, `panel.cfg`) normalmente usam caminhos relativos por profundidade de pastas (`../../../../attachments/...`), que **não precisam mudar**, desde que a profundidade da estrutura de pastas continue a mesma.
7. Gere `manifest.json` (`"content_type": "LIVERY"`, `"package_order_hint": "SIMOBJECTS_PATCH"`) e `layout.json` (listando todos os arquivos copiados com `path`/`size`/`date`; não precisa de `uncompressed_size` se os arquivos não forem `.fsc` compilados, o que é o caso normal de liveries de terceiro).
8. Feche o MSFS completamente, abra de novo (a Community é reescaneada no início).
9. Teste primeiro no Voo Livre, depois no Career.

### 17.3 O que ainda não entendemos (mas funciona mesmo assim)

Não temos uma explicação de engenharia definitiva de *por que* essa combinação específica funciona — só sabemos, empiricamente, que funciona. Hipótese mais provável: o Career, pra aeronaves com `livery.cfg`, deve varrer apenas a(s) pasta(s) "vendor" reconhecida(s) como oficiais daquele avião (por isso pastas de criadores de terceiro tipo `TimHH`/`ryanbatc` nunca funcionaram, não importa o que a gente tentasse nelas), e dentro dessa pasta oficial, escolhe por ordem alfabética do nome (por isso o `!` é necessário — sem ele, perde pra "01"/"default"/etc.).

### 17.4 Ferramenta usada no processo

Toda a criação do pacote (cópia de pastas, edição de `livery.cfg`, geração de `manifest.json`/`layout.json`) foi feita manualmente nesta sessão. Um "Career Livery Manager" real deveria automatizar exatamente esses passos: (1) detectar o simobject/vendor da aeronave escolhida a partir do pacote oficial instalado, (2) pedir ao usuário a pasta da livery de terceiro (ou already-installed), (3) gerar o pacote Community de sobreposição com a estrutura acima, (4) fazer backup/permitir desfazer removendo só essa pasta Community (nunca precisa tocar em Official).

### 17.5 Casos testados

| Aeronave | Livery testada | Tem `livery.cfg`? | Resultado |
|---|---|---|---|
| Cessna Citation Longitude | NetJets N823QS, variante fixa (Community, criador TimHH) | Sim | **Funcionou** ✅ |
| Cirrus SF50 Vision Jet | N93SJ, variante fixa (Community, criador ryanbatc) | Sim | **Funcionou** ✅ |
| Honda HA-420 HondaJet | Dark Blue Stripes (oficial, só renomeando pasta) | Não | Não funcionou ❌ (esperado — sem `livery.cfg` não tem campo de nome pra prefixar) |
| Cessna Citation Longitude | N282N, **apenas a variante Dynamic Registration** (Community, criador ryanbatc) | Sim | **Funcionou** ✅ |
| Cirrus SF50 Vision Jet | N93SJ, **apenas a variante Dynamic Registration** (mesma livery do teste anterior, invertendo qual variante fica com o `!`) | Sim | **Funcionou** ✅ |
| Cessna Citation CJ4 | N460PK (Community, criador ZachB, sem variante DR) | Sim | **Funcionou** ✅ |

### 17.6 Variante "somente Dynamic Registration" (confirmada)

Muitas liveries de terceiro vêm em duas variantes dentro da mesma pasta do criador:
- Uma com **matrícula fixa** (ex.: `C700_N282N`, `SF50_N93SJ`) — nome de cauda gravado na textura.
- Uma **"Dynamic Registration"** (`..._DR`) — usa uma textura gerada dinamicamente pra matrícula (`model.tail`/`model.registration` + `panel.tail`/`panel.registration` com uma textura HTML renderizada em tempo real), e por isso **não tem a textura principal da fuselagem própria** — ela depende de um `fallback.1=` no `texture\texture.cfg` apontando pra pasta irmã (a variante fixa) pra pegar o resto da pintura.

Testamos usar **apenas a variante `_DR`** como a vencedora (com `!` no nome da pasta e no `livery.cfg`), mantendo a pasta da variante fixa **sem** `!` só para servir de fonte de textura pelo `fallback.1` (sem competir na ordenação). **Funcionou perfeitamente nas duas aeronaves testadas** (Longitude e Cirrus). Ou seja, a receita da Seção 17.2 funciona igual tanto pra variante fixa quanto pra variante Dynamic Registration — só é preciso lembrar de manter a pasta-base presente (sem `!`) quando for usar a variante `_DR`, e apontar o `fallback.1` dela pro nome exato dessa pasta-base.

**Recomendação pro programa**: quando o usuário escolher uma livery que tenha variante `_DR`, o programa deveria preferir usar a `_DR` como padrão (matrícula gerada dinamicamente tende a parecer mais "natural"/integrada ao invés de uma matrícula fixa de outro dono), aplicando automaticamente esse padrão "base sem `!` + `_DR` com `!`".

### 17.7 Terceira aeronave confirmada: Cessna Citation CJ4

Testamos a receita numa terceira aeronave, sem variante `_DR` dessa vez (livery com uma pasta só, `model.fuselage`/`panel.fuselage`/`texture.fuselage`, dependendo só de texturas genéricas do sistema via fallback, sem depender de pasta irmã). **Funcionou** igual às outras duas. Isso eleva a confiança de que a receita generaliza pra qualquer aeronave moderna com `livery.cfg` — 3 aeronaves de fabricantes/pacotes diferentes (Asobo/Longitude, Microsoft/Cirrus, Asobo/CJ4), 5 testes no total (contando variantes fixa e DR), 5 sucessos.

## Apêndice A — Pacotes localizados

- A321 oficial: `Official2024\Steam\microsoft-aircraft-a321`
- Longitude oficial: `Official2024\Steam\asobo-passiveaircraft-citation-longitude`
- PC-24 oficial: `Official2024\Steam\asobo-passiveaircraft-pc24`
- CJ4 completo: `Official2024\Steam\asobo-aircraft-cj4`
- CJ4 passive (tráfego AI): `Official2024\Steam\asobo-passiveaircraft-citation-cj4`
- Livery Community A321 (Latam PS-LBA, texto plano, usada como referência de schema): `Community\microsoft-aircraft-a321-LAN-PS-LBA`
- Livery Community 737 MAX (Gol PR-XMT, único exemplo com `dressing_codes`/`Tags` em uso real): `Community\asobo_b737max-GLO-PRXMT`
- Addon legado (schema `[FLTSIM.N]` clássico, para referência/contraste): `Community\flybywire-aircraft-a320-neo`

Total no install: 130 pacotes `asobo-passiveaircraft-*` vs. 89 pacotes `microsoft-aircraft-*`/`asobo-aircraft-*` (aeronaves completas) no `Official2024\Steam`.

## Apêndice C — Addendum: pasta "Roaming" e localização real do Longitude

Depois da primeira versão deste relatório, foi feita uma busca pela pasta de dados de usuário do MSFS 2024 (`%APPDATA%\Microsoft Flight Simulator 2024`), fora de `Official2024`/`Community`. Ela existe em:

```
C:\Users\Antonio\AppData\Roaming\Microsoft Flight Simulator 2024\
```

Dentro dela, em `SimObjects\Airplanes\`, existem **duas** pastas relacionadas ao Longitude:

```
SimObjects/Airplanes/asobo_longitude/
    common/config/state.CFG
    presets/asobo/longitude_passengers/config/     (vazia)

SimObjects/Airplanes/asobo_passiveaircraft_cit_longitude/
    common/config/state.CFG
    presets/asobo/citation_longitude/config/       (vazia)
```

Importante: **isso NÃO é um pacote de conteúdo** (não tem `manifest.json`, `layout.json`, texturas, nem `livery.cfg`). É um **cache de estado em runtime** que o próprio simulador escreve automaticamente sempre que você voa qualquer SimObject, para persistir configurações de painel entre voos (ex.: brilho do AS3000 — é literalmente o que está dentro do `state.CFG`). O simulador cria uma pasta dessas para cada identificador interno de SimObject que já foi "instanciado" em algum voo, mesmo que o pacote de origem esteja compactado dentro de `Official2024`.

Busquei em **todos** os `layout.json` de `Official2024\Steam` e de `Community` por qualquer referência a um SimObject chamado `asobo_longitude` (sem "passiveaircraft") e **não encontrei nenhum pacote instalado nesta máquina que o contenha**. Ou seja: o identificador interno `asobo_longitude` já foi usado nesta instalação (provavelmente numa versão anterior do jogo/uma reinstalação/ou um add-on removido), mas o pacote correspondente **não está mais presente** no disco hoje. Isso não prova que existe um "Longitude completo" escondido — é mais provável que seja resíduo órfão de cache do que evidência de um pacote oculto.

**Conclusão prática, sem especular além do que os dados mostram:** nesta instalação, o único pacote de conteúdo real (com arquivos de textura/manifest) para o Citation Longitude é `Official2024\Steam\asobo-passiveaircraft-citation-longitude`. Não há, nos arquivos, nenhum pacote alternativo "completo" do Longitude — a pasta de cache em Roaming é uma pista órfã, não uma segunda instalação.

Isso significa que a correlação "passive = truque não funciona" apresentada na Seção 5 continua sendo apenas uma hipótese (H3) baseada em UMA amostra (Longitude + PC-24, ambos passive). Não é uma explicação comprovada — só a mais bem correlacionada com os dados que temos. Ela pode perfeitamente estar errada; o experimento da Seção 5 continua sendo o jeito de confirmar ou descartar.

## Apêndice D — Opções concretas para fazer o mod (Longitude)

Dado tudo que sabemos até agora, aqui estão as opções reais, da mais simples/arriscada para a mais robusta/trabalhosa:

**Opção 1 — Replicar o truque do fórum num livery.cfg Community para o Longitude**
Se você conseguir (ou já tiver) uma livery Community para `asobo_passiveaircraft_cit_longitude` (repaint de terceiros, com `livery.cfg` em texto), o primeiro teste barato é só editar `[General] name` para ordenar antes de `"Citation Longitude"` (nome da livery oficial), e ver se o Career reflete a troca. Não sabemos ainda se funciona para essa família de pacote — é o próprio experimento da Seção 5. Baixo esforço, resultado incerto.

**Opção 2 — Replicar o padrão real de Career encontrado no 737 MAX (Gol PR-XMT)**
Junto com (ou em vez de) renomear, adicionar ao `livery.cfg` da livery customizada:
```ini
[Specialization]
dressing_codes = ""

[Tags]
tag.0 = "Licence_Airline"
```
(`dressing_codes = ""` = elegível para todas as carreiras, conforme a documentação oficial). Esse é o único exemplo real, em disco, de alguém usando esses campos deliberadamente — vale testar mesmo sem sabermos ainda o significado exato dos códigos.

**Opção 3 — Pacote Community "overlay" dedicado (a estratégia recomendada na Seção 9)**
Criar um pacote Community novo e próprio (ex. `careerliverymanager-longitude-override`) que referencia o SimObject `asobo_passiveaircraft_cit_longitude` e contém apenas um `livery.cfg` (mais os presets de nome), aplicando as Opções 1+2 combinadas, sem tocar em nenhum addon de terceiros nem no jogo oficial. Essa é a abordagem mais segura para automatizar no programa, porque: (a) nunca edita pacotes que não são seus, (b) sobrevive a updates de Official2024, (c) o "restore" é só apagar a pasta.

**Opção 4 — Descobrir o mecanismo real via engenharia reversa dinâmica (não estática)**
Como o `livery.cfg` oficial está compilado (`.fsarchive`), a única forma de saber com certeza o que faz o Career escolher a livery é observar o jogo rodando: usar Process Monitor (leitura) para ver quais arquivos o `FlightSimulator2024.exe` abre ao trocar de avião no Career, e/ou testar variações controladas (Seção 5) anotando o resultado. Isso é mais trabalho, mas é o único jeito de sair de "hipótese" para "fato confirmado" — recomendo fazer isso antes de eu implementar automação no programa, para não codificarmos um mecanismo errado.

Nenhuma dessas opções foi executada — nada foi alterado nos seus arquivos. Me diga qual você quer tentar primeiro (recomendo Opção 1+2 juntas como primeiro teste manual, porque é rápido e já usa o único exemplo real que temos de `dressing_codes`/`Tags` em uso).

## Apêndice E — Citações exatas da fonte primária (fórum oficial)

Extraído diretamente de https://forums.flightsimulator.com/t/aircraft-liveries-in-career-mode/725803 (não é reprodução controlada, é relato de usuário — tratar como FATO apenas quanto a "isto foi relatado", não quanto a "isto é garantido para qualquer livery"):

- **ArcturusMengsk1** (21/03): observou que o MSFS 2024 parece carregar a primeira livery da lista em ordem alfabética; sugeriu renomear para algo como "AA Wizz" para forçar a ordem.
- **ArcturusMengsk1** (23/03): confirmou funcionar com a livery Community **"A321LR-hawaiian"** (Hawaiian Airlines) no A321, instalada via versão **Microsoft Store** (não Steam), caminho:
  ```
  ...\AppData\Local\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community\A321LR-hawaiian\SimObjects\Airplanes\microsoft-a321\liveries\Hawaiian321\a321lr-hawaiian\
  ```
  Estrutura idêntica à dos pacotes Community A321 que já tínhamos analisado nesta máquina (mesmo `SimObjects\Airplanes\microsoft-a321\liveries\<criador>\<livery>\`).
- **DirtierToast757** (22/03): tentou **renomear a pasta** da livery (não o campo `name` do `livery.cfg`) e obteve "um avião nu" (textura não carregou). Isso é um efeito colateral **diferente** do mecanismo de ordenação — renomear a pasta quebra referências internas de caminho; o método que de fato funcionou foi editar o texto `[General] name=` dentro do `livery.cfg`, mantendo a pasta com o nome original.
- **EndoLanikea** (26/03): testou o mesmo tipo de edição no **Longitude** e no **PC-24** — em ambos "a livery permaneceu inalterada".
- **ArcturusMengsk1** também alertou, em post posterior, que "nem todas as liveries funcionam com esse workaround" — ou seja, mesmo dentro do A321 (a aeronave "que funciona"), o resultado não é garantido para toda e qualquer livery.

Conclusão desta fonte: temos **exatamente um relato positivo** (A321 + livery "A321LR-hawaiian", MS Store) e **dois relatos negativos** (Longitude, PC-24), todos de usuários diferentes, sem reprodução cruzada/controlada. É evidência real, mas fraca estatisticamente — não dá pra descartar que o resultado dependa de detalhes específicos daquela livery/pacote (ex. se ela já tinha ou não `required_tags` corretos, se dependia de cache do sim, etc.), e não apenas do tipo de aeronave.

## 18. Fase 3 — Aeronaves com múltiplas categorias de atividade (C172, Caravan, e outras "Asobo")

Sessão de 2026-09-13. Investigação de um mecanismo diferente do documentado nas Seções 1-17: aeronaves "base"/utilitárias feitas pela Asobo Studio (não parceiros como iniBuilds/Microsoft) têm **múltiplas liveries oficiais organizadas por atividade de carreira**, algo que o Longitude/CJ4/SF50 (testados nas Fases 1-2) não tinham.

### 18.1 Padrão de nomenclatura descoberto

Em `liveries/asobo/` de aeronaves como o Cessna 172SP G1000 (`asobo-aircraft-c172sp-as1000`) e o Cessna 208B Grand Caravan EX, os nomes de pasta seguem `<atividade>_<modo>_<NN>`:

```
cargo_freelance_01
cargo_adaptivergnl_01..05
cargo_static_01
flightseeing_freelance_01
flightseeing_adaptivergnl_01..10
flightseeing_static_01..05
skydive_freelance_01
skydive_adaptiveintl_01
aerialad_freelance_01
aerialad_static_01
official_static_01
```

Nenhuma dessas pastas tem `livery.cfg` (mesmo formato compilado `model.*`/`texture`/`thumbnail` das Fases 1-2). Confirmado o mesmo padrão em outras aeronaves Asobo: Caravan (troca flightseeing/aerialad por medevac/rescue/scientific), AT-802 (agricultural/firefighting), H125 helicóptero (agricultural/cargo/flightseeing/rescue), 737 MAX (commercial/**private** — charter!), XCub, CL-415, ES-30. Aeronaves de parceiros (ex. Pilatus PC-12 NGX, feita pela Microsoft) usam convenção de nomes totalmente diferente (`livery_04_vip_4` etc.) — o padrão `_freelance_/_adaptivergnl_/_static_` parece ser específico de aeronaves construídas pela própria Asobo.

`official_static_01` é a única opção da categoria "official" — sem irmãs "adaptive". Empiricamente (ver 18.3) é usada nos trabalhos de VIP/Private Charter.

### 18.2 SDK oficial encontrado: `[Specialization] dressing_codes`

O usuário encontrou a documentação oficial do SDK do MSFS pro `livery.cfg` (página "Livery Tab / SimObject Editor"). Ela confirma e nomeia formalmente o mecanismo que já tínhamos visto empiricamente na Seção 4 (livery Gol PR-XMT do 737 MAX):

```ini
[Specialization]
dressing_codes = "CAR-PSO, CAR-PLC, CAR-PCC, CAR-PVO"
```

Vazio (`""`) = utilizável em qualquer carreira (era o caso de todas as nossas liveries de teste até aqui, por omissão). Tabela completa de códigos documentada:

| Especialização | Código |
|---|---|
| Medevac - Plane | MED-PLN |
| Cargo (Light/Medium/Heavy/Super Heavy) - Plane | CAR-PSO / CAR-PLC / CAR-PCC / CAR-PVO |
| Remote Cargo Ops - Plane | CAR-PLM |
| Cargo - Rotorcraft | CHT-ROH |
| Aerial Construction - Rotorcraft | CHT-AEC |
| Scientific Research - Plane | DIC-SCR |
| Passenger Transport - Plane/Rotorcraft | COF-PCC / COF-ROT |
| Charter (Private/VIP/VIP Airliner) - Plane | PRC-PSO / PRC-PLC / PRC-PCC |
| Search & Rescue - Plane/Rotorcraft/(Hoist) | SAR-PLN / SAR-ROT / SAR-ROI |
| Firefighting (Initial/Extended) - Plane | FIR-INA / FIR-EXA |
| Skydive Aviation - Plane | SKP-PLN |
| Agricultural - Plane/Rotorcraft | AEA-PLN / AEA-ROT |
| Aerial Advertising - Plane | AAD-PLN |
| First Flight - Plane | FIF-PLN |
| Flightseeing - Plane/Rotorcraft | TOR-PLN / TOR-ROT |
| Ferry Flight - Plane | FEF-PLN |

Também documentado `[Tags] tag.N` como mecanismo separado ("independente dos tags do attachment.cfg") pra "disponibilizar a livery em atividades específicas e agrupar liveries" — não testado ainda.

### 18.3 Testes empíricos (C172, todos manuais, fora do app)

Metodologia: pacotes Community de teste, cada um sobrescrevendo o caminho **exato** de uma pasta oficial (`simobjects/airplanes/asobo_c172sp_g1000/liveries/asobo/<nome-oficial-exato>/`, sem prefixo `!`, substituindo o conteúdo em vez de competir por nome novo). Liveries de terceiro usadas: N733EC (KFS, tem variante G1000 "fina" com fallback pra pasta irmã `asobo_c172sp` não-G1000) e N9110E (jeffs, autocontida).

| Teste | Configuração | Resultado |
|---|---|---|
| `!`-prefix nova pasta em `flightseeing_freelance_!N733EC` (mesma lógica das Fases 1-2) | 1 pacote | **Não funcionou** — a pasta em pool (com irmãs adaptive/static) não responde ao truque de nome novo, só a sobrescrita exata |
| Sobrescrita exata de `flightseeing_freelance_01` com N733EC | 1 pacote isolado | **Funcionou** |
| Sobrescrita exata de `cargo_freelance_01` com N9110E | 1 pacote isolado | **Funcionou** |
| `!`-prefix nova pasta em `official_static_!N733EC` (categoria sem irmãs) | 1 pacote isolado | **Funcionou** (VIP mostrou N733EC) |
| `official_static` sozinho, SEM nenhum outro pacote tocando o SimObject | 0 outros pacotes | Mostra o padrão do jogo (nunca testado sem nenhum override presente) |
| VIP com só o pacote de **cargo** ativo (nada tocando `official_static`) | 1 pacote (cargo) | VIP mostrou **N9110E** (a do cargo!) |
| VIP com só o pacote de **flightseeing** ativo | 1 pacote (flightseeing) | VIP mostrou **N733EC** (a do flightseeing!) |
| Cargo + Flightseeing **juntos**, cada um sobrescrevendo sua própria pasta exata, sem pacote de VIP no meio | 2 pacotes | **Cargo "tomou conta" dos dois** — flightseeing também virou a livery do cargo |
| Adicionar `[Specialization] dressing_codes` correto (TOR-PLN/CAR-PSO/PRC-PLC) nos 3 pacotes, todos ativos juntos | 3 pacotes | Piorou: flightseeing voltou ao padrão do jogo, cargo mostrou uma livery **adaptive oficial aleatória** (não a nossa), VIP replicou o resultado do flightseeing. Reverteu-se a mudança. |

### 18.4 Conclusões desta fase

1. **`official_static` (VIP) não parece ter conteúdo próprio de verdade** — em todos os testes, ele sempre espelhou qualquer que fosse o **outro** pacote Community ativo no momento pro mesmo SimObject, nunca mostrou conteúdo dele mesmo de forma independente. Comportamento consistente, não é bug de teste.
2. **Um único pacote Community, sozinho, sobrescrevendo o caminho exato de uma pasta de categoria com "pool" (`cargo_freelance_01`, `flightseeing_freelance_01`), funciona.** Isso é reprodutível e confiável.
3. **Dois pacotes Community diferentes, cada um remendando (`SIMOBJECTS_PATCH`) o mesmo SimObject ao mesmo tempo (mesmo em pastas exatas diferentes), não convivem corretamente** — um deles (aparentemente o carregado por último/algum critério de prioridade ainda não identificado) passa a valer pra tudo, inclusive pra pasta que ele nem deveria tocar. Isso aconteceu tanto com 3 pacotes quanto com apenas 2.
4. **`[Specialization] dressing_codes` não resolveu o problema acima** — na verdade piorou o resultado. Hipótese ainda não testada: talvez `dressing_codes` só seja relevante pro modo **Employee** (liveries "adaptive" de empresa), não pro modo **Freelance**, e ao definir um código específico numa livery que ocupa um slot "freelance", ela pode ter deixado de casar com o critério que a lógica de freelance realmente usa (que talvez espere vazio/sem restrição), tirando-a da disputa.
5. **Ainda não testado**: se o problema do item 3 é especificamente sobre **múltiplos pacotes Community separados** tocando o mesmo SimObject, ou se persiste quando um **único pacote** define várias pastas de categoria ao mesmo tempo (`cargo_freelance_01` E `flightseeing_freelance_01` dentro do mesmo `manifest.json`/pacote). Esse é o próximo teste mais barato e mais informativo a fazer.

### 18.4b Teste decisivo: um único pacote com as duas pastas

Testado exatamente o experimento sugerido no item 5 acima: um único pacote Community (`career-livery-TEST-c172-combined`, um `manifest.json` só) contendo `cargo_freelance_01` (N9110E) **e** `flightseeing_freelance_01` (N733EC) juntos, sem nenhum outro pacote tocando o SimObject.

**Resultado**: as duas categorias (e o VIP) mostraram **N9110E**. Isso **descarta a hipótese de "múltiplos pacotes brigando por prioridade de carregamento"** — mesmo dentro de um pacote só, com cada livery na sua pasta exata, uma delas "vence" e é aplicada pra tudo. O mecanismo de resolução do Career, uma vez que existe qualquer `livery.cfg` de terceiro em qualquer lugar do namespace `liveries/asobo/` daquele SimObject, parece escolher **uma única livery "vencedora"** pro avião inteiro, ignorando em qual pasta de categoria especificamente ela está — não faz lookup independente por pasta/atividade como a estrutura de nomes sugeria.

### 18.5 Próximos passos propostos (a fazer, não executado ainda)

Até agora só testamos uma variável de cada vez (só nome de pasta, só sobrescrita exata, só `dressing_codes`). Falta testar **combinações**, e também tornar nosso pacote mais parecido estruturalmente com o que a Asobo realmente gera:

1. **`[Tags] tag.N`** — mecanismo documentado como separado do `dressing_codes`, nunca testado.
2. **Process Monitor durante troca de atividade no Career** — ver exatamente qual arquivo o jogo abre (mesma técnica da Seção 14.1).
3. **Descobrir a regra do "vencedor único"** (Seção 18.4b) — é sempre a última em ordem alfabética? A primeira? Precisa ser previsível.
4. **Teste combinado, tudo junto ao mesmo tempo**, em vez de isolado:
   - sobrescrita exata da pasta (`cargo_freelance_01`, sem `!`)
   - **+** `[Specialization] dressing_codes` correto
   - **+** `[Tags] tag.N` (item 1)
   - **+** `[Selection] required_tags` batendo com os tags reais que o `attachment.cfg`/`attached_objects.cfg` daquele SimObject define (não testamos ainda se `required_tags = "wheels"` é suficiente ou se falta algum tag específico de atividade vindo dos attachments)
   - **+** estrutura de arquivo o mais parecida possível com o padrão oficial (mesmo os oficiais não tendo `livery.cfg`, pode ser que outros metadados/campos do `layout.json` ou `manifest.json` importem, ex. `package_order_hint` diferente de `SIMOBJECTS_PATCH`)

   Ideia: já que testar uma coisa de cada vez não resolveu, testar tudo simultaneamente pode revelar se o mecanismo exige a combinação completa, não uma peça isolada.

### 18.6 Estado prático atual (2026-09-13)

Por enquanto, o resultado mais confiável e replicável é: **uma aeronave "Asobo" com múltiplas categorias aceita uma livery customizada única, aplicada de forma consistente independente da atividade** (sobrescrevendo qualquer uma das pastas de categoria funciona, e o VIP acompanha). Ainda não conseguimos fazer **duas liveries diferentes coexistirem simultaneamente** (uma pra cargo, outra pra flightseeing) sem uma "vencer" a outra. Se isso não for resolvido, a funcionalidade prática pro app seria "uma skin pro avião inteiro" (já é isso que o app faz hoje pra Longitude/CJ4/etc.), não "uma skin por atividade" — o que ainda seria uma melhoria real pra aeronaves como C172/Caravan que hoje não têm suporte nenhum (formato compilado sem `livery.cfg` nas pastas oficiais).

## 19. Fase 4 — Mecanismo resolvido: `[Tags]` + `[Specialization]` juntos (2026-09-17)

### 19.1 Documentação oficial encontrada

Lendo a documentação oficial do SDK (`livery_cfg.htm`, `aircraft_cfg.htm`, e principalmente `Careers/General_Career_Information/Career_Additional_Information.htm#liveries` — "Livery Application"), o mecanismo de seleção do Career é este, documentado (não mais reverse-engineering):

- **Modo Employee (tela de especialização)**: pega a **primeira livery encontrada cujo `dressing_codes` bate** com a especialização. Sem correspondência → primeira livery disponível.
- **Modo Freelancer (Company Fleet, Aircraft Store — é o caso do avião que o próprio jogador possui)**: procura uma livery que bata **as duas coisas ao mesmo tempo**:
  1. `[Specialization] dressing_codes` batendo com a especialização da empresa/atividade.
  2. `[Tags]` tendo a tag `"Freelance"` **E** uma tag de licença da atividade (`Licence_Tour`, `Licence_CargoTransport`, etc.).
  Se nenhuma livery bater as duas → cai pro fallback: primeira livery encontrada que só tenha a tag `"Freelance"` (ignora qual licença).
- **"Certifications"**: pega a primeira livery que encontrar, **sem checar tag nenhuma**. Isso explica por que `official_static` sempre respondeu ao truque velho do `!` sem precisar de tag nenhuma — essa categoria não é resolvida pelo sistema de tags.

Lista completa de tags de licença documentadas: `Licence_Tour`, `Licence_SkydiveSport`, `Licence_CargoTransport`, `Licence_AgriculturalAviation`, `Licence_AerialAdvertising`, `Licence_Airline`, `Licence_PrivateCharter`, `Licence_Firefighting`, `Licence_SearchAndRescue`, `Licence_Medevac`, `Licence_AerialConstruction`.

Isso explica retroativamente **todos** os resultados estranhos da Fase 3: nunca tínhamos colocado a tag `"Freelance"` + a licença certa nas nossas liveries de teste — só `dressing_codes` sozinho (Seção 18.4, item 4) ou nada. Sem a tag `Freelance`, nossa livery nunca era candidata válida no modo Freelancer, e o jogo caía no fallback pra qualquer outra livery oficial que tivesse essa tag — batendo exatamente com os resultados aleatórios/cruzados que vimos.

### 19.2 Teste confirmado — CARGO E FLIGHTSEEING DIFERENTES AO MESMO TEMPO ✅

Testado no C172 (mesmo pacote Community, `career-livery-TEST-c172-combined`, sobrescrita exata das duas pastas, sem `!`):

```ini
# flightseeing_freelance_01 (livery N733EC)
[Specialization]
dressing_codes = "TOR-PLN"

[Tags]
tag.0 = "Freelance"
tag.1 = "Licence_Tour"
```

```ini
# cargo_freelance_01 (livery N9110E)
[Specialization]
dressing_codes = "CAR-PSO"

[Tags]
tag.0 = "Freelance"
tag.1 = "Licence_CargoTransport"
```

**Resultado**: trabalho de cargo freelance mostrou N9110E, trabalho de flightseeing freelance mostrou N733EC — **cada categoria com a sua própria livery, simultaneamente, sem uma "vencer" a outra**. Resolve de vez o problema da Seção 18.4b. VIP (`official_static`) continuou no padrão do jogo nesse teste, porque não foi tocado — precisa do tratamento separado (rota "Certifications", sem tags) já validado antes.

### 19.2b Teste final — as três categorias juntas, no mesmo avião ✅✅✅

Terceiro teste, adicionando `official_static_01` (livery San Juan Airlines, rota "Certifications" sem tags) ao mesmo pacote que já tinha cargo e flightseeing com as tags corretas:

**Resultado**: as três mostraram corretamente sua própria livery ao mesmo tempo — cargo=N9110E, flightseeing=N733EC, VIP/charter=San Juan Airlines. **Confirmado 100%, sem nenhuma "vencer" as outras.** A receita da Seção 19.3 está validada de ponta a ponta.

### 19.3 Receita definitiva (válida em qualquer avião desse sistema, não só C172)

Como isso é comportamento **documentado oficialmente** (não uma peculiaridade do C172), deve generalizar pra qualquer aeronave Asobo com esse sistema de categorias — incluindo o 737 MAX, que tem exatamente a mesma estrutura (`commercial_freelance_01`, `private_freelance_01`, sem `livery.cfg` nas oficiais).

Pra cada categoria de atividade que o avião tiver:
1. Sobrescrever a pasta oficial **exata** (ex.: `cargo_freelance_01`), sem prefixo `!`.
2. No `livery.cfg` da livery de terceiro, adicionar:
   ```ini
   [Specialization]
   dressing_codes = "<código da tabela da Seção 18.2>"

   [Tags]
   tag.0 = "Freelance"
   tag.1 = "<licença da atividade>"
   ```
3. Pra categoria "official_static" (VIP/genérica, sem pool): continua a receita antiga, pasta nova com `!`, sem precisar de tags.

### 19.3b Peso/classe do cargo e charter: não precisa descobrir por avião

A SDK (`Careers/Cargo_Transport.htm`) confirma que `CAR-PSO`/`CAR-PLC`/`CAR-PCC`/`CAR-PVO` são definidos pela **massa de payload** da aeronave (limiares em kg), não por algo legível nos arquivos que temos acesso. Em vez de calcular isso por avião, usar a lista completa de uma vez, já que `dressing_codes` aceita múltiplos valores separados por vírgula (documentado): `dressing_codes = "CAR-PSO, CAR-PLC, CAR-PCC, CAR-PVO"`. Mesma ideia pra Charter (`PRC-PSO, PRC-PLC, PRC-PCC`). Evita precisar de tabela de peso por avião.

### 19.4 Decisão de escopo pro app (2026-09-17)

O app só vai oferecer customização pra pastas com `_freelance_` (o que o avião do próprio jogador usa) e pro `official_static` (rota "Certifications", genérica/VIP). Pastas `_static_`/`_adaptivergnl_`/`_adaptiveintl_` sozinhas (sem uma `_freelance_` irmã na mesma atividade) são do modo **Employee** (avião de empresa, não o do jogador) — fora do escopo, não vamos mexer nelas.

## 20. Fase 5 — Bug real reportado por usuário no 737 MAX (2026-09-18)

Um usuário da comunidade (Tom, via flightsim.to) testou a v1.2.0 no 737 MAX com liveries reais (KLM, Tarom, YR-MAX) e reportou dois sintomas distintos. Análise dos logs que ele mandou.

### 20.1 Sintoma 1 (bug real, corrigido): livery "pisca" entre padrão e customizada dependendo do ângulo da câmera

Reportado como: "depending on the angle that i position my exterior camera to, the livery changes from the default to KLM and back" e "the plane in headquarters is bugged, BUT the livery is perfectly fine during an actual mission".

**Causa raiz confirmada**: no modo com Dynamic Registration (`useDr=true`) + atividade, o `PackageBuilder` copiava a pasta-base (pintura completa) como **irmã solta** dentro de `liveries/<vendor>/`, sem nenhum `[Tags]`/`[Specialization]` (igual sempre fez pro Longitude/CJ4, onde isso era inofensivo porque aquele avião não usa o sistema de tags). Só que pra aviões com o sistema de atividades (737 MAX), qualquer pasta solta sem tag no namespace compartilhado é candidata válida pra rota "Certifications" (primeira encontrada, sem checar tag) — e o hangar/exibição parece usar essa rota, diferente do momento da missão (que usa a lógica completa de Tags documentada na Seção 19). Duas liveries (a com tag certa + a solta sem tag) competindo pelo mesmo lugar = comportamento inconsistente dependendo do contexto/ângulo.

**Correção**: a pasta-base do DR agora fica **aninhada dentro da pasta da atividade vencedora** (`<activity_folder>/_fallback_base/`), nunca mais solta em `liveries/<vendor>/`. O `fallback.1` do `texture.cfg` da variante DR aponta pra esse caminho aninhado (`..\_fallback_base\texture`) em vez do caminho antigo de pasta-irmã. Testado com uma livery sintética base+DR — confirmado que `liveries/asobo/` fica com **apenas** a pasta da atividade, nada mais solto.

### 20.2 Sintoma 2 (não é bug do app, limitação da livery escolhida): só o motor (e às vezes o "shark fin" da asa) muda, resto continua padrão

Reportado como: "for every other livery that i tried it only changes my engines, and on some my tail shark fins... but the rest of the plane is still default".

**Hipótese mais provável**: o 737 MAX é uma aeronave modular com peças separadas (fuselagem, asas, motores — como o A321 documentado nas Seções 1-4), cada uma com seus próprios arquivos de textura nomeados individualmente. O mecanismo de `fallback.1` funciona por **arquivo**: se um arquivo de textura não existe na pasta vencedora nem na pasta de fallback, o motor do jogo usa o padrão dele mesmo pra aquele arquivo específico. Se a livery de terceiro escolhida (`KLM`, `Tarom`, etc.) for uma repintura **parcial** (só decalques de matrícula/motor, sem fornecer os arquivos de textura completos da fuselagem/asas na pasta-base), o resultado esperado é exatamente esse: só as partes com arquivo próprio mudam, o resto cai no padrão oficial.

Isso não é uma falha do nosso mecanismo — é sobre a livery de terceiro em si ser uma repintura completa ou não. Recomendação pro usuário: testar com uma livery explicitamente anunciada como "full repaint" (não só "registration"/"tail number"), e também testar com "Use Dynamic Registration" desmarcado (usando só a pasta-base direto, sem depender de fallback nenhum) pra isolar se o problema é a completude da pasta-base em si.

### 20.3 Investigação com a livery real da KLM (2026-09-18) — achado mais preciso que 20.2

O usuário mandou um print de uma mancha verde perto da raiz da asa/barriga numa livery da KLM (`mattyr197/klm`) na activity `commercial_freelance_01`, e confirmou explicitamente que **não** era uma pasta DR (então a correção da Seção 20.1 não se aplica a esse caso) e que a mancha era a **pintura original por baixo aparecendo**, não uma cor de "textura não encontrada".

Extraindo a livery real, ela só trazia `model.airframe/`, `model.wing_l/`, `model.wing_r/` (geometria + textura) e nenhum `texture.cfg`. Comparando com a pasta oficial `commercial_freelance_01` (e também `official_static_01`, pra confirmar que não é exclusivo dessa activity), **toda** pasta de livery oficial do 737 MAX vem com 7 peças modeladas: `model.airframe`, `model.wing_l`, `model.wing_r`, `model.wing_c` (asa **central** — a peça que liga as duas asas na fuselagem, ou seja, exatamente a raiz da asa/barriga), `model.tail`, `model.landinggearl`, `model.landinggearr`.

**Por que funciona perfeito no Free Flight mas não na activity**: no Free Flight a livery vive na própria pasta dela (`mattyr197/klm`), que nunca teve pretensão de cobrir as 7 peças — o fallback dela pras peças que faltam cai limpo no visual padrão do avião. Já no nosso pacote, a gente copia essa mesma livery **por cima** do nome da pasta oficial da activity — que antes tinha as 7 peças completas. O empacotamento em pacotes Community substitui a pasta inteira (não faz merge arquivo-a-arquivo com o conteúdo Oficial que tinha aquele mesmo nome), então as peças que a KLM não cobre (`wing_c`, `tail`, `landinggearl`, `landinggearr`) ficam **sem nenhuma cobertura** nesse slot específico — nem a da KLM, nem a oficial que existia ali antes — daí o fallback "cru" (sem pintura) só nessas peças.

**Correção implementada** (`PackageBuilder.FillMissingOfficialActivityFiles`, escopado só pro 737 MAX via `B737MaxSimObjectName`): depois de copiar a livery de terceiros pra dentro da pasta oficial da activity, o `PackageBuilder` compara com a pasta oficial correspondente (`AircraftInfo.VendorPath` + `activity.OfficialFolderName`) e completa, arquivo a arquivo, **só o que estiver faltando** — nunca sobrescreve o que a livery de terceiros já trouxe. `livery.cfg`, a pasta `thumbnail/` e qualquer `texture.cfg` oficial são sempre ignorados (precisam continuar sendo os da livery de terceiros: tags de activity, nome do pacote e imagem de preview). Testado com a livery real da KLM: as 7 peças passam a existir no pacote final, `wing_c`/`tail`/`landinggearl`/`landinggearr` vêm da pasta oficial, `airframe`/`wing_l`/`wing_r`/`livery.cfg` continuam sendo exatamente os da KLM. Escopado só pro `asobo_b737max` porque os outros aviões com activities (C172, Caravan, AT-802, H125, XCub, CL-415, ES-30) são modelos de corpo único mais simples, sem essa divisão em várias peças exteriores separadas — não têm esse tipo de lacuna. Shipado na v1.2.1 (junto com a correção da Seção 20.1), ainda sem confirmação visual em jogo (o desenvolvedor não tem o 737 MAX comprado atualmente pra testar; depende de feedback do usuário que reportou o bug original).

## 22. Fase 6 — Formato `.fsc` decifrado, e a raiz de verdade do problema do `wing_c` (2026-09-19)

Depois do usuário testar a v1.2.2 (Seção 21) no 737 MAX real, a mancha continuou verde mesmo com o backfill de peças e a neutralização de textura. Investigação mais a fundo, seguindo uma pergunta direta do usuário ("tem como a gente ler o arquivo `.fsc`?"):

### 22.1 `.fsc` é só zlib — não é formato proprietário nem criptografado

Os arquivos `model.<peça>/livery_lodNN.gltf.fsc` / `.bin.fsc` que vêm com toda pasta de livery oficial começam com os bytes `78 DA` — assinatura clássica de um stream **zlib** (deflate, nível de compressão alto). Descomprimindo com `System.IO.Compression.ZLibStream` (nativo do .NET 8, sem dependência externa) o resultado é um JSON glTF válido e legível. Ou seja: o `.gltf.fsc` é literalmente um `.gltf` comprimido, nada mais.

### 22.2 Por que o backfill anterior (Seção 21) não funcionava

O sistema de merge de livery do jogo (documentado em "Modular SimObject Merging" → "Merge Process For Liveries") só entende glTF/bin **soltos, não-comprimidos** (ou um `livery.xml` apontando pra eles). Copiar o `.fsc` bruto pra dentro da pasta da livery (o que a v1.2.2 inicial fazia) colocava um arquivo com nome/local certo, mas em formato que o merge engine não consegue ler — então a peça simplesmente não é processada pelo merge, e cai no visual padrão do próprio attachment (que, para o `wing_c`, coincide seguidamente com a arte de alguma livery de demonstração oficial do slot usado, como a "Air Infinity" do `commercial_freelance_01`).

### 22.3 A textura referenciada não fica onde o glTF "diz"

Inspecionando o JSON decodificado do `model.wing_c` do `official_static_02` ("Boeing Business Jets"): os materiais (`LIVERY_BBJ_LOGO_TAIL_1/2`) referenciam uma imagem via `images[].uri = "..\MODEL.WING_C\737_MAX_8_BBJ_LOGO_TAIL.PNG.KTX2"`. Mas esse arquivo **não existe** dentro da pasta `model.wing_c` — ele mora na pasta `texture/` comum do vendor, com nome em minúsculo (`737_max_8_bbj_logo_tail.png.ktx2`). Confirma que a URI embutida no glTF é só um rótulo residual da ferramenta de exportação da Asobo, não um caminho literal de arquivo — a resolução em tempo real busca pelo **nome do arquivo apenas**, na mesma cadeia de fallback de pastas de textura já documentada na Seção 22.2/Merge Process, case-insensitive.

### 22.4 Correção implementada

`PackageBuilder.CopyModelPartDecompressingFsc` substitui a cópia bruta: para cada arquivo `.fsc` dentro da pasta de peça sendo trazida (seja porque a peça está totalmente ausente, seja porque o `livery.xml` da própria livery aponta pra algo que não existe — Seção 20.3/21), descomprime com `ZLibStream` e grava o `.gltf`/`.bin` real (sem o sufixo `.fsc`). Ao decodificar um `.gltf`, o JSON é parseado (`System.Text.Json`) pra extrair `images[].uri`, pega só o nome do arquivo, e busca (case-insensitive) esse nome na pasta `texture/` da MESMA fonte (`official_static_02`, a pasta neutra da Seção 21.4) — copiando o arquivo (+ seu `.json` de metadados) pro pacote se ainda não existir lá. Testado com a KLM: `model.wing_c` final tem `livery_lod01/02/03.gltf`/`.bin` de verdade (sem `.fsc`), e a textura `737_max_8_bbj_logo_tail.png.ktx2` foi trazida automaticamente pra `texture/`. Ainda sem confirmação visual em jogo no momento do commit — depende do próximo teste do usuário original que reportou o bug.

## 23. Fase 7 — Usando o attachment real (a fonte que o Free Flight usa) (2026-09-19)

Depois do usuário testar a v1.2.4 em jogo de verdade e confirmar: **de perto ficou branco (correto), de longe mostrou a Qatar Airways (`official_static_10`, como esperado — a fonte que escolhemos)**. Ele então perguntou: por que não usar os `attachments/` da pasta do SimObject em vez de qualquer pasta de livery — já que é isso que o Free Flight usa de verdade quando nada cobre a peça?

### 23.1 A pasta `attachments/asobo/part_exterior_wing_c` é a fonte real, sem marca nenhuma

Comparando com o material de qualquer livery (`CUSTOM_COLOR_XX` cor sólida da Air Infinity, ou `Livery_Qatar_Airways_2` texturizado): o attachment próprio (`part_exterior_wing_c/model/wing_c_lod01.gltf.fsc`) tem **14 materiais completos e realistas** (`WING_DETAIL`, `RIVETS_BLEND`, `WING_DTL_CC_METALLIC`, `Frost_Standard`, `WING_L_CC_METALLIC`, `WING_R_CC_METALLIC`, decals, etc.) — todos usando texturas **genéricas e compartilhadas** (`737_MAX_8_AIRFRAME_WING_DETAIL_ALBD`, `..._WING_LEFT_ALBD`, `..._WING_RIGHT_ALBD`, `..._RIVET_ALBD`, `PAINTFLAKES_*`, `FROST_*`) — os MESMOS nomes que já existem em `part_exterior_airframe/texture/`, e que só ALGUMAS liveries oficiais (`official_static_02`, `private_static_01/02`) redeclaram por conta própria. A `commercial_freelance_01` e a `official_static_10` nunca tocam nesses arquivos — deixam pro attachment resolver sozinho.

Ou seja: esse é o "avião cru" de verdade — a superfície real da asa, sem depender de nenhuma companhia aérea específica. É exatamente por isso que o Free Flight sempre parece bem quando uma livery não cobre `wing_c`: ele está mostrando essa geometria+textura genérica, não "sem nada".

### 23.2 Estrutura confirmada do pacote (útil de lembrar)

```
asobo_b737max/
├── common/            → base do avião, cockpit (não tem wing_c aqui)
├── attachments/asobo/  → part_exterior_wing_c, part_exterior_airframe, part_exterior_tail,
│                         part_exterior_wing_l/r, part_exterior_engine_left/right,
│                         part_exterior_landinggear_front/rearbay/rearleft/rearright (4 peças
│                         separadas — NÃO bate 1:1 com o "landinggearl"/"landinggearr" que as
│                         liveries usam)
├── presets/asobo/      → só 2: b737max8_bbj e b737max8_passengers
└── liveries/asobo/     → commercial_freelance_01, official_static_01-10, private_freelance_01, etc.
```

Confirmado: **nenhum arquivo `.cfg` legível existe em lugar nenhum** do pacote oficial (tudo compilado num binário opaco, provavelmente o mesmo formato `.fsarchive` do Apêndice B) — não dá pra ler o threshold exato de distância de cada LOD, só inferir pela contagem de arquivos.

Também confirmado: o attachment de `wing_c` tem **LOD01, 02, 03 E 04** (quatro níveis) — mais do que qualquer livery jamais fornece (só 01-03). Ou seja, nem o attachment cobre o alcance MAIS próximo (LOD00) — esse gap é estrutural, não uma falha de nenhuma livery específica.

### 23.3 Correção implementada (v1.2.5)

`FillMissingOfficialActivityFiles` agora tenta, em ordem: **(1)** o attachment do próprio SimObject (`attachments/asobo/part_exterior_<peça>/model/`) — só quando o nome bate 1:1 (`wing_c`, `tail`, `airframe`, `wing_l`, `wing_r`, `engine_left/right`; **NÃO** cobre trem de pouso, que segue peça-a-peça diferente entre attachment e livery) — **(2)** o esquema neutro (`official_static_10`) — **(3)** a própria pasta oficial da activity, como antes.

Como o attachment de `wing_c` não tem pasta `texture/` própria (as texturas genéricas moram em `part_exterior_airframe/texture/`, pasta irmã), `CopyModelPartDecompressingFsc` agora aceita uma pasta extra de fallback de textura, usada só quando a fonte é um attachment.

Testado com a TUI: `model.wing_c` final tem as 4 peças reais do attachment (`wing_c_lod01-04.gltf/bin`, descomprimidos) + o LOD00 sintético branco (Seção 22.4, inalterado) + todas as texturas genéricas (`wing_detail`, `wing_left`, `wing_right`, `rivet`) trazidas corretamente da pasta irmã `part_exterior_airframe/texture/`. Confirmado: os 14 materiais do LOD00 sintético saem com `baseColorFactor:[1,1,1,1]` e zero `baseColorTexture` restante.

**Ainda não testado em jogo** no momento do commit desta seção — v1.2.4 (fonte `official_static_10`) foi confirmada funcionando visualmente pelo usuário; v1.2.5 (fonte = attachment real) ainda depende de teste.

### 23.4 v1.2.5 testada em jogo: attachment real é REJEITADO pelo merge (2026-09-19)

Usuário testou a v1.2.5 de verdade (JAL, activity commercial) — **voltou a ficar verde**, exatamente como antes de qualquer fix. Conclusão: o arquivo do attachment (`part_exterior_wing_c/model/wing_c_lod01.gltf`) não é aceito pelo sistema de merge de livery como override válido — provavelmente porque é um modelo completo com **dados de animação** embutidos (visto na Seção 23.3: `accessorAnimationInput`/`accessorAnimationRotations` logo no início do JSON), diferente do formato simples de "decal" que uma livery de verdade usa (só malha+material, sem animação). O merge rejeita silenciosamente e a peça cai de volta no material original da activity (sem erro visível, sem log — só "não aconteceu nada").

**v1.2.7 — solução final (até agora)**: usar a geometria de uma livery oficial REAL (`official_static_10`, Qatar Airways) como "doadora de forma" — já que ESSE formato é comprovadamente aceito pelo merge (confirmado funcionando na v1.2.4) — mas agora forçando `baseColorFactor:[1,1,1,1]` e removendo `baseColorTexture` em **TODOS os LODs** (00/01/02/03), não só no LOD0 sintético. Resultado: a peça deve ficar branca em qualquer distância, sem nenhuma cor/arte real da Qatar aparecendo, usando um arquivo que o jogo genuinamente aceita processar.

**Lição geral**: pra essa peça especificamente, só um arquivo no formato exato de "livery override" (simples, sem animação) é aceito pelo merge — não basta ser "geometria real e sem marca", tem que ser um arquivo do tipo certo. O attachment do próprio SimObject, mesmo sendo a fonte mais "correta" em teoria, não serve pra esse propósito.

## Apêndice B — Formato `.fsarchive`

Assinatura binária observada: `52 41 53 41` = ASCII `"RASA"`, seguida de bytes que parecem versão/contagem (`02 00 03 00 01 00 00 00 95 03 00 00`) e depois dados de alta entropia (aparentam estar comprimidos e/ou criptografados). Não foi feita nenhuma tentativa de decodificação — apenas documentado que o formato existe e é opaco. Isso não é um formato ZIP padrão nem um container reconhecível pelas ferramentas usadas nesta investigação.
