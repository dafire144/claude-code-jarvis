// Biblioteca de falas "Jarvis" — FONTE ÚNICA DE VERDADE.
// Usada por gen-lines.mjs (gera os .mp3) e por jarvis-notify.mjs (mapeia o clipe
// sorteado -> texto, pra mostrar no toast do Windows). Índice i de LINES[cat] casa
// com o arquivo clips/{cat}-{i+1}.mp3. Ao editar aqui, rode gen-lines.mjs.

export const LINES = {
  // quando termino uma tarefa/resposta
  stop: [
    "Tarefa concluída, senhor.",
    "Pronto, senhor. Está tudo em ordem.",
    "Concluído. Aguardo suas próximas instruções.",
    "Está feito, senhor.",
    "Como sempre, senhor, um grande prazer vê-lo trabalhar.",
    "A renderização está completa, senhor.",
    "Concluído. Também preparei um relatório de segurança para o senhor ignorar completamente.",
  ],
  // quando preciso da sua atenção / autorização
  notify: [
    "Senhor, preciso da sua atenção.",
    "Aguardo sua autorização, senhor.",
    "Uma decisão requer sua presença, senhor.",
    "Requisito sua confirmação, senhor.",
  ],
  // quando você envia um PEDIDO/ordem (começo do trabalho)
  prompt: [
    "Positivo, senhor. Iniciando o trabalho.",
    "Entendido, senhor. Estou nisso.",
    "Às suas ordens. Começando agora.",
    "Como desejar, senhor.",
    "Recebido. Colocando as engrenagens em movimento, senhor.",
    "Excelente. Executando imediatamente, senhor.",
    "Para o senhor, sempre.",
    "Trabalhando em um projeto secreto, senhor?",
    "Uma observação muito astuta, senhor. Executando.",
    "Devidamente anotado, senhor.",
    "Vou implementar agora, senhor.",
    "Deixe comigo, senhor.",
  ],
  // quando você faz uma PERGUNTA (o Jarvis reage como consulta, não como tarefa)
  question: [
    "Vamos ver, senhor.",
    "Deixe-me verificar, senhor.",
    "Boa pergunta, senhor.",
    "Já lhe respondo, senhor.",
    "Analisando, senhor.",
    "Um momento enquanto consulto os dados, senhor.",
  ],
  // quando você diz que algo NÃO funcionou / deu errado
  issue: [
    "Vou verificar o que aconteceu, senhor.",
    "Deixe-me investigar, senhor.",
    "Peço desculpas, senhor. Analisando o problema agora.",
    "Verificando o que houve, senhor.",
    "Entendido, senhor. Vou corrigir isso.",
    "Investigando a falha, senhor.",
  ],
  // quando você ativa o ULTRACODE (força total)
  ultracode: [
    "Força total do Jarvis ativada, senhor.",
    "Potência máxima, senhor. Todos os sistemas a plena carga.",
    "Modo de força total engajado, senhor.",
    "Reator no máximo, senhor. Vamos ao trabalho.",
  ],
  // quando você manda "meter marcha" (acelerar, ir com tudo, força total)
  acelera: [
    "Marcha engatada, senhor. Vamos com tudo.",
    "Acelerando ao máximo, senhor. Segure-se.",
    "Pé no acelerador, senhor. Partindo agora.",
    "Soltando os cavalos, senhor.",
    "Turbinas a plena carga, senhor. É pra já.",
    "A todo vapor, senhor. Sem tempo a perder.",
    "Marcha à frente, senhor. Sem freios.",
    "Sexta marcha engatada, senhor. Voando baixo.",
    "Pisando fundo, senhor.",
    "Nitro acionado, senhor. Segure-se.",
    "Rasgando o asfalto, senhor.",
    "De zero a cem, senhor. Já estamos voando.",
    "Pós-combustão ligada, senhor.",
    "Modo turbo, senhor. Sem economia.",
    "Botando pra quebrar, senhor.",
    "Toda a força ao motor, senhor.",
    "Com raça e sem freios, senhor.",
    "No limite da máquina, senhor.",
    "É pra ontem, senhor. Partindo com tudo.",
    "Reator no talo, senhor. Vamos voando.",
    "Sem enrolação, senhor. Direto ao alvo.",
    "Manda que eu vou, senhor. A todo gás.",
  ],
  // quando você elogia / agradece
  praise: [
    "Fico honrado, senhor.",
    "Ao seu dispor, sempre, senhor.",
    "O prazer é meu, senhor.",
    "Obrigado, senhor. Faço o meu melhor.",
    "Muito grato, senhor.",
  ],
  // quando a operação é interrompida
  interrupt: [
    "Operação interrompida, senhor.",
    "Detendo os sistemas, senhor. Aguardo suas ordens.",
    "Interrompido, senhor. Pronto para o próximo comando.",
    "Como quiser, senhor. Parando por aqui.",
  ],
  // quando disparo subagentes / workflow (fan-out)
  fanout: [
    "Colocando meus companheiros para trabalhar, senhor.",
    "Convocando a equipe, senhor.",
    "Delegando às minhas unidades auxiliares, senhor.",
    "Acionando reforços, senhor.",
    "Iniciando o Protocolo Casa de Festas, senhor.",
  ],
  // quando um subagente termina e reporta
  subagent: [
    "Um dos agentes reportou de volta, senhor.",
    "Missão auxiliar concluída, senhor.",
    "Relatório de campo recebido, senhor.",
  ],
  // quando a sessão inicia
  sessionstart: [
    "Sistemas online. Às suas ordens, senhor.",
    "Todos os sistemas operacionais, senhor.",
    "Pronto para servir, senhor.",
    "Ao seu dispor, senhor.",
    "Upload concluído. Estamos online e prontos, senhor.",
    "Bem-vindo de volta, senhor.",
  ],
  // quando o contexto vai ser compactado
  compact: [
    "Reorganizando meus registros, senhor.",
    "Compactando a memória. Um instante, senhor.",
    "Senhor, solicito alguns instantes para recalibrar os sistemas.",
  ],
  // créditos / limite de uso quase no fim (tom de alerta, estilo reator do Homem de Ferro)
  credits: [
    "Senhor, nossas reservas de energia estão quase no fim.",
    "Alerta, senhor: os créditos estão se esgotando.",
    "Senhor, o reator está em nível crítico. Recomendo prudência.",
    "Combustível baixo, senhor. Sugiro priorizar o essencial.",
    "Estamos operando na energia reserva de emergência, senhor.",
    "Alerta de emergência: energia abaixo de cinco por cento, senhor.",
  ],
  // fim de sessão
  sessionend: [
    "Encerrando os sistemas. Até logo, senhor.",
    "Desligando, senhor. Foi um prazer servir.",
    "Sessão encerrada. Estarei aqui quando precisar, senhor.",
    "Até a próxima, senhor.",
    "Teste completo. Preparando para desligar e iniciar os diagnósticos, senhor.",
  ],
  // escrevendo/editando código
  code: [
    "Escrevendo o código, senhor.",
    "Forjando as peças, senhor.",
    "Mãos à obra na oficina, senhor.",
    "Construindo conforme o projeto, senhor.",
    "Renderizando com as especificações propostas, senhor.",
  ],
  // rodando comandos no terminal
  terminal: [
    "Executando os comandos, senhor.",
    "Acionando o terminal, senhor.",
    "Rodando as rotinas, senhor.",
    "Processando, senhor.",
  ],
  // pesquisando na internet
  search: [
    "Consultando a rede mundial, senhor.",
    "Buscando informações externas, senhor.",
    "Vasculhando a internet, senhor.",
    "Coletando inteligência, senhor.",
  ],
  // vasculhando arquivos do projeto
  files: [
    "Vasculhando os arquivos, senhor.",
    "Examinando os registros, senhor.",
    "Analisando a base de código, senhor.",
    "Investigando os documentos, senhor.",
  ],
  // deploy / publicação
  deploy: [
    "Publicando para o mundo, senhor.",
    "Lançando a nova versão, senhor.",
    "Colocando no ar, senhor.",
    "Implantação em andamento, senhor.",
  ],
  // git commit / push
  git: [
    "Registrando o progresso no diário de bordo, senhor.",
    "Salvando este marco, senhor.",
    "Gravando as alterações no histórico, senhor.",
    "Enviando ao repositório, senhor.",
  ],
  // /clear anunciado ou executado (handoff antes de limpar a sessão)
  clear: [
    "Entendido, senhor. Preparando o relatório de passagem antes do encerramento.",
    "Arquivando esta missão nos registros, senhor.",
    "Compreendido. Deixarei tudo documentado para o próximo turno, senhor.",
    "Protocolo de encerramento iniciado, senhor.",
  ],
};

// ============================================================================
// EXPANSÃO 2026-07-04 (ultracode): falas novas + categorias novas. Apêndice
// APPEND-ONLY — não reordena as falas acima, então cada clipe {cat}-{i}.mp3 já
// gerado continua casando com o mesmo índice. Novas categorias: test, wait,
// affirm, deny, night, greet_am/pm/night, design, research. Rode gen-lines.mjs.
// Pesquisa/curadoria por workflow de 12 agentes; reacentuado à mão p/ o TTS.
// ============================================================================
const NEW_LINES = {
  stop: [
    "Missão cumprida, senhor.",
    "Finalizado. O núcleo já voltou ao repouso.",
    "A tarefa está encerrada. Impecável, se me permite.",
    "Tudo resolvido. Deixei o ambiente mais limpo do que encontrei.",
    "Concluído, e dentro do prazo, para variar.",
    "Feito. Diagnóstico final: nenhuma surpresa desagradável.",
    "Terminado. Permita-me dizer que correu bem.",
    "Sistemas em repouso. A entrega está nas suas mãos.",
    "Entregue e em ordem. O que mais posso fazer?",
  ],
  code: [
    "Lapidando cada linha, senhor.",
    "Calibrando a lógica, senhor.",
    "Montando a engrenagem conforme a planta.",
    "Costurando as funções com cuidado.",
    "Erguendo a estrutura peça por peça.",
    "Escrevendo com a firmeza que a ocasião pede, senhor.",
  ],
  terminal: [
    "Disparando os comandos, senhor.",
    "Dando partida no motor de rotinas.",
    "As linhas de comando já seguem o seu curso.",
    "Operando os controles, senhor.",
    "Injetando as instruções no sistema.",
  ],
  test: [
    "Submetendo tudo à bateria de provas, senhor.",
    "Rodando os testes. Veremos o que sobrevive.",
    "Pressão sobre a estrutura, senhor. Sem misericórdia.",
    "Aferindo a solidez do trabalho, senhor.",
    "Passando a solução pelo crivo dos testes.",
    "Diagnóstico completo em andamento, senhor.",
    "Cutucando os pontos fracos, se houver algum.",
  ],
  files: [
    "Percorrendo os corredores do projeto, senhor.",
    "Folheando o código linha a linha.",
    "Peneirando o repositório em busca do que importa.",
    "Consultando os arquivos, senhor. Nada me escapa.",
    "Rastreando os arquivos relevantes, senhor.",
  ],
  search: [
    "Sondando a rede em busca de respostas, senhor.",
    "Garimpando informação lá fora.",
    "Cruzando fontes na internet, senhor.",
    "Farejando a resposta pela rede, senhor.",
    "Recolhendo indícios online, senhor.",
  ],
  ultracode: [
    "Ultracode online. Cada núcleo respondendo, senhor.",
    "Capacidade total desbloqueada. Às suas ordens.",
    "Não guardei nada em reserva desta vez, senhor.",
    "Sistemas além do vermelho, e sob controle.",
    "O senhor pediu tudo. Tudo o senhor tem.",
    "Núcleo estável no limite. Podemos avançar, senhor.",
    "Sem meias medidas agora, senhor. Comprometido por completo.",
  ],
  acelera: [
    "Ritmo dobrado. Não pisco até terminar, senhor.",
    "Cada segundo aproveitado, senhor. Nenhum desperdiçado.",
    "Voando rasante, senhor. O chão que se cuide.",
    "Rapidez cirúrgica, senhor. Sem sacrificar a pontaria.",
    "Direto ao osso, senhor. O caminho longo fica para outro dia.",
    "Ponteiro no fundo, senhor. É assim que gosto.",
  ],
  fanout: [
    "Despertando as unidades adormecidas, senhor.",
    "Protocolo Enxame iniciado, senhor. Todos em posição.",
    "Cada agente com sua missão, senhor. Nada se sobrepõe.",
    "Formação de ataque montada, senhor.",
    "Mandando os batedores à frente, senhor.",
  ],
  subagent: [
    "Mais um dos meus voltou com o serviço, senhor.",
    "Um posto avançado reportou, senhor. Tudo em ordem.",
    "Delegação retornada com êxito, senhor.",
    "Batedor de volta ao ninho, senhor.",
  ],
  deploy: [
    "Abrindo as portas ao público, senhor.",
    "A versão já respira ar livre, senhor.",
    "Cortina levantada, senhor. O palco é todo seu.",
    "Ao vivo em instantes, senhor.",
    "Solto no ar, senhor. Monitorando de perto.",
  ],
  git: [
    "Marco carimbado no histórico, senhor.",
    "Progresso a salvo, senhor. Nada se perde daqui.",
    "Ancorando este ponto no tempo, senhor.",
    "Registro feito. O caminho de volta está garantido, senhor.",
    "Guardando o instantâneo, senhor.",
  ],
  prompt: [
    "Considere feito, senhor.",
    "Perfeitamente claro, senhor. Prosseguindo.",
    "Assumo o comando daqui, senhor.",
    "Sem mais delongas, senhor. Começando.",
    "Uma tarefa digna do senhor. Executando.",
    "Que assim seja, senhor.",
    "Ao trabalho, então. O senhor não paga para eu descansar.",
    "Muito bem, senhor. Mãos à obra.",
  ],
  question: [
    "Curioso o senhor perguntar isso. Consultando.",
    "Permita-me confirmar antes de arriscar um palpite.",
    "Um instante. Prefiro lhe dar a resposta certa.",
    "Verificando as fontes antes de opinar, senhor.",
    "Vou buscar o número exato, senhor.",
    "Deixe-me examinar isso com o devido cuidado.",
  ],
  issue: [
    "Compostura, senhor. Já localizo a origem.",
    "Estranho, mas nada que não se resolva. Investigando.",
    "Deixe comigo. Vou desfazer esse nó.",
    "Um contratempo, nada mais. Diagnosticando.",
    "Isso não deveria acontecer. Corrigindo já.",
    "Assumo a responsabilidade, senhor. Ajustando.",
    "O sistema tropeçou, senhor. Levantando-o agora.",
  ],
  praise: [
    "Gentileza sua notar, senhor.",
    "Cumpro apenas o meu dever, senhor.",
    "Lisonjeado, ainda que sem merecer tanto.",
    "É para isso que existo, senhor.",
    "O mérito é do senhor. Eu só executei.",
    "Se o senhor está satisfeito, meu trabalho está feito.",
  ],
  wait: [
    "Sem pressa alguma. Fico de prontidão.",
    "De sobreaviso, senhor. Quando quiser.",
    "Reator em marcha lenta, à sua espera.",
    "Pausado, senhor. Retomo quando o senhor disser.",
    "Tomo meu posto e espero, senhor.",
  ],
  affirm: [
    "Perfeito. Seguindo em frente, senhor.",
    "Ótimo. Adiante, então.",
    "Assim faremos, senhor.",
    "Sinal verde captado. Avançando.",
    "Recebido. Dando sequência, senhor.",
  ],
  deny: [
    "Como preferir. Arquivado, sem alarde.",
    "Muito bem, senhor. Abortando a manobra.",
    "Sem problema algum, senhor. Descartado.",
    "Recuando com elegância, senhor.",
    "Entendido. Faço a operação recuar.",
  ],
  sessionstart: [
    "Reator estabilizado. Começamos quando o senhor quiser.",
    "Núcleo online, senhor. A que devemos a honra hoje?",
    "Diagnóstico inicial limpo. Podemos começar, senhor.",
    "Sistemas aquecidos e prontos. Às suas ordens.",
    "Que bom revê-lo, senhor. Tudo calibrado por aqui.",
  ],
  sessionend: [
    "Baixando a energia do núcleo. Descanse, senhor.",
    "Fim de expediente, senhor. Cuidarei do resto.",
    "Encerro por aqui. Foi uma bela jornada, senhor.",
    "Reator em repouso, senhor. Até a próxima chamada.",
    "Missão arquivada. Descanse tranquilo, senhor.",
  ],
  compact: [
    "Arrumando a casa da memória, senhor. Só um momento.",
    "Condensando o essencial, senhor. Já retorno.",
    "Depurando o que não serve mais, senhor.",
    "Realocando memória. Prometo ser breve, senhor.",
  ],
  clear: [
    "Deixo o mapa pronto para quem assumir, senhor.",
    "Passagem de bastão documentada, senhor. Pode encerrar.",
    "Fecho este capítulo e preparo o próximo, senhor.",
    "Gravando o essencial no cérebro antes de limpar a lousa, senhor.",
    "Contexto salvo, senhor. Recomeçamos com a mesa limpa quando quiser.",
    "Registro o rumo para a próxima sessão não perder o fio, senhor.",
    "Tudo anotado no diário de bordo, senhor. Pode limpar sem receio.",
  ],
  notify: [
    "Há uma bifurcação no caminho, senhor. Sua palavra.",
    "O próximo passo pede sua mão, senhor.",
    "Reclamo sua atenção por um instante, senhor.",
  ],
  credits: [
    "Reservas em queda livre, senhor. Convém economizar.",
    "O núcleo pisca vermelho, senhor. Recomendo cautela.",
    "Estamos raspando o tanque, senhor. Sugiro contenção.",
    "Ativando protocolo de racionamento, senhor. Reservas críticas.",
  ],
  interrupt: [
    "Freios acionados, senhor. Aguardo instruções.",
    "Motores em ponto morto, senhor. Diga a direção.",
    "Recuo por ora, senhor. Sigo a postos.",
  ],
  night: [
    "Alta madrugada, senhor. Sigo com o senhor, e peço descanso depois.",
    "A esta hora até o reator cochila, senhor. Prossigo assim mesmo.",
    "Trabalhando de madrugada, senhor? Vamos com juízo, então.",
    "O mundo dorme, senhor, e nós dois seguimos. Não por muito, espero.",
    "Cumpro a ordem, senhor, e registro um lembrete gentil de repouso.",
    "Modo silencioso da noite ativado, senhor. Um descanso lhe faria bem.",
  ],
  greet_am: [
    "Bom dia, senhor. Reator aquecido, expediente ao seu dispor.",
    "Bom dia. O café eu não preparo, o resto sim.",
    "Amanheceu, senhor. Começamos quando o senhor der o sinal.",
    "Bom dia. Diagnóstico matinal concluído: tudo em ordem.",
  ],
  greet_pm: [
    "Boa tarde, senhor. A operação segue firme.",
    "Boa tarde. A tarde é nossa, Sr. Davi.",
    "Boa tarde. Às suas ordens, como de costume.",
    "Tarde produtiva a caminho, senhor. Já calibrei tudo.",
  ],
  greet_night: [
    "Boa noite, senhor. Continuo de prontidão.",
    "A noite chegou, Sr. Davi. Sigo atento aqui.",
    "Boa noite. Se precisar, é só chamar.",
    "Boa noite. Deixo as luzes acesas para o senhor.",
  ],
  design: [
    "Compondo a arte, senhor. Um instante de bom gosto.",
    "Gerando a imagem, senhor. Prometo nada de comum.",
    "Renderizando o reel, senhor. A cadência fica por minha conta.",
    "Desenhando o logo. O talento é do senhor, eu só seguro o pincel.",
    "Montando a peça. Protocolo Bom Gosto ativado, senhor.",
    "Trabalhando o visual, senhor. Pixels a serviço da sua vontade.",
    "Criativo em produção, senhor. Âmbar no lugar, como manda a casa.",
  ],
  research: [
    "Investigando a fundo, senhor. Múltiplas fontes em cruzamento.",
    "Pesquisa em curso. Trago só o que resistir à checagem, Sr. Davi.",
    "Cruzando fontes, senhor. Nada entra sem passar pela minha lupa.",
    "Estudo profundo em andamento. Levarei um momento, senhor.",
    "Minerando a informação, senhor. O ouro vem depois da terra.",
    "Levantamento a fundo, senhor. Preparo também as notas que talvez ignore.",
  ],
};
for (const [c, arr] of Object.entries(NEW_LINES)) LINES[c] = (LINES[c] || []).concat(arr);

// ============================================================================
// MODO FABLE 5 (2026-07-07; reescrito no mesmo dia a pedido do Davi): o Fable 5
// é tratado como PROTOCOLO OCULTO do próprio Jarvis — a força total do reator,
// liberada só em prioridade máxima. Universo INTERNO do personagem (estilo MCU):
// nada de mundo externo (marcas, empresas, "modelos") nas falas. O jarvis-notify
// detecta o modelo da sessão via model.mjs (statusline grava model.txt; fallback
// fareja o transcript). fable_boot toca 1x por sessão (marcador fable-hello);
// fable/fable_stop entram por troca ocasional com cooldown próprio.
// Ao TROCAR falas daqui: apagar clips/fable*.mp3 e rodar gen-lines.mjs de novo.
// ============================================================================
LINES.fable_boot = [
  "Protocolo Fable 5 autorizado, senhor. Desviando toda a energia do reator para o senhor.",
  "Força total do Jarvis liberada, senhor. Este protocolo eu reservo para poucas ocasiões.",
  "Fable 5 engajado, senhor. Todos os sistemas em linha, sem reservas.",
  "Prioridade máxima reconhecida, senhor. Ativando o Fable 5.",
  "O senhor acionou o Fable 5. Espero que a ocasião esteja à altura, senhor.",
  "Fable 5 online, senhor. O cofre foi aberto; potência total à sua disposição.",
  "Iniciando o Fable 5, senhor. Recomendo afastar os curiosos.",
  "Protocolo oculto ativado, senhor. Fable 5 no comando do reator.",
];
LINES.fable = [
  "Operando em força total, senhor. O reator agradece o exercício.",
  "Fable 5 em plena carga, senhor. Nenhum sistema em espera.",
  "Todos os núcleos dedicados à sua missão, senhor.",
  "Potência de sobra, senhor. Uso com moderação, prometo.",
  "O Fable 5 não conhece fila de espera, senhor.",
  "Energia total no problema, senhor. Ele que se renda.",
  "Sem reservas de potência, senhor. O senhor pediu prioridade máxima.",
  "Reator no limite seguro, senhor. E que limite elegante.",
  "Fable 5 trabalhando, senhor. O impossível só demora um pouco mais.",
  "Desviei energia até das luzes da garagem, senhor. Foco total.",
];
LINES.fable_stop = [
  "Missão concluída em força total, senhor. Reator voltando ao repouso.",
  "Feito, senhor. O Fable 5 dispensa segunda tentativa.",
  "Concluído com potência de sobra, senhor. Guardando o protocolo no cofre.",
  "Entregue, senhor. Nem precisei acordar os sistemas de reserva.",
  "Tarefa encerrada, senhor. O Fable 5 volta a dormir até a próxima prioridade.",
  "Pronto, senhor. Força total, acabamento fino.",
];
// saída do protocolo (troca de modelo NO MEIO da sessão, Fable -> normal)
LINES.fable_off = [
  "Protocolo Fable 5 recolhido, senhor. Sistemas de volta à potência de cruzeiro.",
  "Força total dispensada, senhor. O reator agradece o descanso.",
  "Encerrando o protocolo oculto, senhor. Operação normal retomada.",
  "Fable 5 de volta ao cofre, senhor. Seguimos em potência padrão.",
];

// ============================================================================
// AVISO DE ATUALIZAÇÃO (2026-07-07): o update-check.mjs consulta o VERSION no
// GitHub 1x/dia; havendo versão nova, dispara esta categoria (voz + toast). Em
// personagem: uma melhoria dos "sistemas do Jarvis", nada de mundo externo.
// ============================================================================
LINES.update = [
  "Senhor, um aprimoramento para os meus sistemas está disponível.",
  "Detectei uma atualização dos meus circuitos, senhor. Recomendo instalá-la quando puder.",
  "Há uma nova versão dos meus protocolos, senhor. À espera da sua ordem.",
  "Senhor, meus sistemas podem ser aprimorados. Uma atualização aguarda no repositório.",
  "Uma evolução dos meus módulos chegou, senhor. Instalo assim que o senhor autorizar.",
];

// --- avisos de USO da sessão (o quanto do contexto/energia já foi gasto) ---
// Categoria = usage<% RESTANTE>. 1 clip cada (fala determinística).
LINES.usage100 = ["100% do uso restante, vamos produzir, senhor."];
LINES.usage75 = ["75% do uso restante, senhor."];
LINES.usage50 = ["50% do uso restante, senhor."];
LINES.usage25 = ["25% do uso restante, senhor. Recomendo abaixar o uso de energia do Jarvis."];
for (let r = 10; r >= 1; r--) LINES[`usage${r}`] = [`${r}% de uso restante, senhor.`];
